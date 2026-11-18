using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pspsps.Server.Auth;
using Pspsps.Server.Telemetry;
using Pspsps.Server.Telemetry.Browser;
using RequestTelemetry = Pspsps.Server.Telemetry.RequestTelemetryExtensions;

namespace Pspsps.Server.Test.Telemetry.Browser;

/// <summary>POST /api/client-telemetry and the page's mark, on a test server set up as Program.cs sets them up.</summary>
public sealed class ClientTelemetryEndpointTests
{
    private static readonly Uri Endpoint = new(ClientTelemetry.Path, UriKind.Relative);

    private sealed class Server : IAsyncDisposable
    {
        public required WebApplication App { get; init; }
        public required HttpClient Client { get; init; }
        public required FakeCollector Collector { get; init; }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await App.DisposeAsync();
            Collector.Dispose();
        }
    }

    private static async Task<Server> StartAsync(bool enabled = true, int permits = 60, Guid? player = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ClientTelemetry:Enabled"] = enabled ? "true" : "false",
            ["ClientTelemetry:OtlpEndpoint"] = "http://collector.test:4318",
            ["ClientTelemetry:PermitsPerMinute"] = permits.ToString(),
            ["OTEL_TRACES_SAMPLER_ARG"] = "0.25",
        });
        var collector = new FakeCollector();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddClientTelemetry(builder.Configuration, ClientTelemetryOtlpTests.Telemetry());
        builder.Services.AddHttpClient<ClientTelemetryForwarder>().ConfigurePrimaryHttpMessageHandler(() => collector);
        var app = builder.Build();
        if (player is { } id)
        {
            // Signed in, as the game's cookie would make it.
            app.Use((context, next) =>
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(PlayerClaims.PlayerIdType, id.ToString())], "test"));
                return next(context);
            });
        }

        app.UseClientTelemetryMark();
        app.UseRateLimiter();
        app.MapClientTelemetry();
        app.MapGet("/", () => Results.Content("<!doctype html>", "text/html"));
        app.MapGet("/api/data", () => Results.Json(new { a = 1 }));
        await app.StartAsync();
        return new Server { App = app, Client = app.GetTestClient(), Collector = collector };
    }

    private static HttpRequestMessage Post(object body, string site = "same-origin")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = JsonContent.Create(body) };
        request.Headers.Add("Sec-Fetch-Site", site);
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0");
        return request;
    }

    [Test]
    public async Task ABatch_IsTakenAndForwardedToTheCollector()
    {
        await using var server = await StartAsync();

        using var response = await server.Client.SendAsync(Post(ClientTelemetryValidationTests.Batch()));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        var paths = new[] { (await server.Collector.NextAsync()).Path, (await server.Collector.NextAsync()).Path };
        Assert.That(paths, Is.EquivalentTo(new[] { "/v1/traces", "/v1/logs" }));
    }

    [Test]
    public async Task Off_IsNotFound_SoTheBrowserStops()
    {
        await using var server = await StartAsync(enabled: false);

        using var response = await server.Client.SendAsync(Post(ClientTelemetryValidationTests.Batch()));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task FromAnotherSite_IsForbidden()
    {
        await using var server = await StartAsync();

        using var response = await server.Client.SendAsync(Post(ClientTelemetryValidationTests.Batch(), site: "cross-site"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task ABatchTheBrowserCouldntHaveSent_IsABadRequest()
    {
        await using var server = await StartAsync();

        using var invalid = await server.Client.SendAsync(Post(ClientTelemetryValidationTests.Batch() with
        {
            Logs = [ClientTelemetryValidationTests.Log() with { Level = "fatal" }],
        }));
        using var notJson = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = new StringContent("{", System.Text.Encoding.UTF8, "application/json") };
        notJson.Headers.Add("Sec-Fetch-Site", "same-origin");
        using var broken = await server.Client.SendAsync(notJson);
        using var text = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = new StringContent("hello") };
        text.Headers.Add("Sec-Fetch-Site", "same-origin");
        using var plain = await server.Client.SendAsync(text);
        // With its length said up front (fetch says it), as Kestrel would also refuse one without, past the limit.
        using var huge = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(new string(' ', ClientTelemetryLimits.BodyBytes + 1), System.Text.Encoding.UTF8, "application/json"),
        };
        huge.Headers.Add("Sec-Fetch-Site", "same-origin");
        using var tooBig = await server.Client.SendAsync(huge);

        Assert.Multiple(() =>
        {
            Assert.That(invalid.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(broken.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(plain.StatusCode, Is.EqualTo(HttpStatusCode.UnsupportedMediaType));
            Assert.That(tooBig.StatusCode, Is.EqualTo(HttpStatusCode.RequestEntityTooLarge));
        });
    }

    [Test]
    public async Task PastThePermitsAMinute_IsTooManyRequests()
    {
        await using var server = await StartAsync(permits: 2);
        var empty = new ClientTelemetryBatch();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            using var response = await server.Client.SendAsync(Post(empty));
            statuses.Add(response.StatusCode);
        }

        Assert.That(statuses, Is.EqualTo(new[] { HttpStatusCode.NoContent, HttpStatusCode.NoContent, HttpStatusCode.TooManyRequests }));
    }

    [Test]
    public async Task ThePermits_AreCountedPerPlayer_EvenForAGuest()
    {
        await using var server = await StartAsync(permits: 1, player: Guid.NewGuid());

        using var first = await server.Client.SendAsync(Post(new ClientTelemetryBatch()));
        using var second = await server.Client.SendAsync(Post(new ClientTelemetryBatch()));

        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests), "signed in isn't unlimited: anyone can make a guest");
    }

    [Test]
    public void Partitions_ArePerPlayerOrPerAddress()
    {
        var player = Guid.NewGuid();
        var signedIn = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(PlayerClaims.PlayerIdType, player.ToString())], "test")),
        };
        var signedOut = new DefaultHttpContext();
        signedOut.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");

        Assert.Multiple(() =>
        {
            Assert.That(ClientTelemetry.PartitionFor(signedIn, 60).PartitionKey, Is.EqualTo($"player:{player}"));
            Assert.That(ClientTelemetry.PartitionFor(signedOut, 60).PartitionKey, Is.EqualTo("address:203.0.113.7"));
            Assert.That(PlayerClaims.GetPlayerId(signedIn.User), Is.EqualTo(player), "the claim the game's cookie carries");
        });
    }

    [Test]
    public async Task ThePage_IsMarked_WithTheShareOfHubCallsToTrace()
    {
        await using var server = await StartAsync();

        using var page = await server.Client.GetAsync(new Uri("/", UriKind.Relative));
        using var data = await server.Client.GetAsync(new Uri("/api/data", UriKind.Relative));

        Assert.Multiple(() =>
        {
            Assert.That(page.Headers.GetValues(ClientTelemetry.ServerTimingHeader), Is.EqualTo(new[] { "pspsps-telemetry;desc=\"0.25\"" }));
            Assert.That(data.Headers.Contains(ClientTelemetry.ServerTimingHeader), Is.False, "only pages");
        });
    }

    [Test]
    public async Task APageLoadAnsweredWithoutAContentType_IsMarkedToo()
    {
        await using var server = await StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/data", UriKind.Relative));
        request.Headers.Add("Sec-Fetch-Dest", "document");

        using var response = await server.Client.SendAsync(request);

        Assert.That(response.Headers.Contains(ClientTelemetry.ServerTimingHeader), Is.True);
    }

    [Test]
    public async Task Off_ThePageIsntMarked()
    {
        await using var server = await StartAsync(enabled: false);

        using var page = await server.Client.GetAsync(new Uri("/", UriKind.Relative));

        Assert.That(page.Headers.Contains(ClientTelemetry.ServerTimingHeader), Is.False);
    }

    [TestCase(0.1, "pspsps-telemetry;desc=\"0.1\"")]
    [TestCase(1, "pspsps-telemetry;desc=\"1\"")]
    [TestCase(2, "pspsps-telemetry;desc=\"1\"")]
    public void TheMark_SaysTheShare(double ratio, string expected) =>
        Assert.That(ClientTelemetry.Mark(ratio), Is.EqualTo(expected));

    [Test]
    public void TheBatches_AreLeftOutOfTheRequestLog()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = ClientTelemetry.Path;

        Assert.That(RequestTelemetry.GetLevel(context, 1, null), Is.EqualTo(Serilog.Events.LogEventLevel.Verbose));
        Assert.That(ClientTelemetry.IsTelemetryPath("/api/me"), Is.False);
    }

    [Test]
    public void Enabled_NeedsAnEndpoint()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ClientTelemetry:Enabled"] = "true" })
            .Build();
        using var services = new ServiceCollection()
            .AddSingleton(TimeProvider.System)
            .AddClientTelemetry(configuration, ClientTelemetryOtlpTests.Telemetry())
            .BuildServiceProvider();

        Assert.That(() => services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ClientTelemetryOptions>>().Value,
            Throws.InstanceOf<Microsoft.Extensions.Options.OptionsValidationException>());
    }
}

public sealed class BrowserMetricsTests
{
    [Test]
    public void Numbers_AreRecordedInTheirUnits_OnTheBrowsersMeter()
    {
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var recorded = new List<(string Instrument, double Value)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == BrowserMetrics.MeterName)
                    l.EnableMeasurementEvents(instrument);
            },
        };
        listener.SetMeasurementEventCallback<double>((instrument, value, _, _) => recorded.Add((instrument.Name, value)));
        listener.Start();
        var metrics = new BrowserMetrics(services.GetRequiredService<IMeterFactory>());

        metrics.Record(
        [
            new ClientVital { Name = "LCP", Value = 1500 },
            new ClientVital { Name = "CLS", Value = 0.05 },
            new ClientVital { Name = "FPS", Value = 58.5 },
            new ClientVital { Name = "LONG_TASK", Value = 120 },
        ]);

        Assert.That(recorded, Is.EqualTo(new[]
        {
            ("pspsps.browser.lcp", 1.5),
            ("pspsps.browser.cls", 0.05),
            ("pspsps.browser.fps", 58.5),
            ("pspsps.browser.long_task.duration", 0.12),
        }));
        Assert.That(MetricsAndTracingExtensions.AdditionalMeters, Does.Contain(BrowserMetrics.MeterName), "exported");
    }
}

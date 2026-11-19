using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pspsps.Server.Game;
using Pspsps.Server.Telemetry.Browser;

namespace Pspsps.Integration.Test;

/// <summary>The browsers' telemetry, end to end in the real server: to the collector, and into the hub's traces.</summary>
public sealed class BrowserTelemetryTests
{
    private static GameFactory Game => PostgresFixture.Game;

    private static double Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    [Test]
    public async Task ABatch_IsForwardedToTheCollector_AsOtlpJson_AsTheGamesBrowser()
    {
        var traceId = ActivityTraceId.CreateRandom().ToHexString();
        var spanId = ActivitySpanId.CreateRandom().ToHexString();
        var batch = new
        {
            version = "abc1234",
            spans = new[] { new { traceId, spanId, name = "GameHub/Move", kind = "client", start = Now - 20, end = Now, status = "ok" } },
            logs = new[]
            {
                new
                {
                    time = Now, level = "error", message = "GET /auth/callback?code=secret answered 502", traceId, spanId,
                    attributes = new Dictionary<string, object> { ["error.kind"] = "http", ["http.response.status_code"] = 502 },
                },
            },
            vitals = new[] { new { name = "LCP", value = 900.0 } },
        };
        using var client = Game.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(ClientTelemetry.Path, UriKind.Relative))
        {
            Content = JsonContent.Create(batch),
        };
        request.Headers.Add("Sec-Fetch-Site", "same-origin");
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (test)");

        using var response = await client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        var traces = await Game.Collector.PostWith("/v1/traces", traceId);
        var logs = await Game.Collector.PostWith("/v1/logs", traceId);
        var resource = traces["resourceSpans"]![0]!["resource"]!["attributes"]!.AsArray()
            .ToDictionary(a => a!["key"]!.GetValue<string>(), a => a!["value"]!["stringValue"]!.GetValue<string>());
        var span = traces["resourceSpans"]![0]!["scopeSpans"]![0]!["spans"]![0]!;
        var line = logs["resourceLogs"]![0]!["scopeLogs"]![0]!["logRecords"]![0]!;
        Assert.Multiple(() =>
        {
            Assert.That(resource, Does.ContainKey("service.component").WithValue("browser"));
            Assert.That(resource, Does.ContainKey("service.version").WithValue("abc1234"));
            Assert.That(resource, Does.ContainKey("user_agent.original").WithValue("Mozilla/5.0 (test)"));
            Assert.That(span["spanId"]!.GetValue<string>(), Is.EqualTo(spanId));
            Assert.That(line["severityText"]!.GetValue<string>(), Is.EqualTo("ERROR"));
            Assert.That(line["body"]!["stringValue"]!.GetValue<string>(), Is.EqualTo("GET /auth/callback?code=REDACTED answered 502"),
                "redacted again on the server");
        });
    }

    [Test]
    public async Task ThePageLoad_IsMarked_SoTheBrowserKnowsToSend()
    {
        using var client = Game.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/", UriKind.Relative));
        request.Headers.Add("Sec-Fetch-Dest", "document");

        using var response = await client.SendAsync(request);

        Assert.That(response.Headers.GetValues(ClientTelemetry.ServerTimingHeader).Single(), Does.StartWith("pspsps-telemetry;desc="));
    }

    [Test]
    public async Task AHubCall_WithTheBrowsersTraceparent_ContinuesItsTraceOnTheServer()
    {
        var traceId = ActivityTraceId.CreateRandom();
        var browserSpan = ActivitySpanId.CreateRandom();
        var stopped = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Microsoft.AspNetCore.SignalR.Server",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);
        var (_, cookie) = await Game.SignInGuestAsync();
        await using var hub = await ConnectAsBrowserAsync(cookie, $"00-{traceId.ToHexString()}-{browserSpan.ToHexString()}-01");

        await hub.InvokeAsync<IReadOnlyList<WorldSummary>>("GetWorlds");

        // The hub's span ends just after it answers: waited for.
        Activity? call = null;
        for (var tries = 0; tries < 100 && call is null; tries++)
        {
            call = stopped.FirstOrDefault(a => a.TraceId == traceId);
            if (call is null)
                await Task.Delay(50);
        }

        Assert.That(call, Is.Not.Null, "the hub's span for the call, in the browser's trace");
        Assert.Multiple(() =>
        {
            Assert.That(call!.DisplayName, Does.EndWith("/GetWorlds"));
            Assert.That(call.ParentSpanId, Is.EqualTo(browserSpan), "a child of the browser's span");
            Assert.That(call.Kind, Is.EqualTo(ActivityKind.Server));
        });
    }

    /// <summary>
    /// A hub connection that sends what the browser sends (client/src/telemetry/tracing.ts): every invocation with a
    /// traceparent in its headers.
    /// </summary>
    private static async Task<HubConnection> ConnectAsBrowserAsync(string cookie, string traceparent)
    {
        var builder = new HubConnectionBuilder()
            .WithUrl(new Uri(Game.Server.BaseAddress, "/hubs/game"), options =>
            {
                options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => Game.Server.CreateHandler();
                options.Headers["Cookie"] = cookie;
            });
        var json = new JsonHubProtocol(Options.Create(new JsonHubProtocolOptions { PayloadSerializerOptions = JsonOptions.Web }));
        builder.Services.AddSingleton<IHubProtocol>(new TraceparentProtocol(json, traceparent));
        var hub = builder.Build();
        await hub.StartAsync();
        return hub;
    }

    /// <summary>JSON, with <paramref name="traceparent"/> in every invocation's headers.</summary>
    private sealed class TraceparentProtocol(IHubProtocol inner, string traceparent) : IHubProtocol
    {
        public string Name => inner.Name;

        public int Version => inner.Version;

        public TransferFormat TransferFormat => inner.TransferFormat;

        public bool IsVersionSupported(int version) => inner.IsVersionSupported(version);

        public bool TryParseMessage(ref ReadOnlySequence<byte> input, IInvocationBinder binder, [NotNullWhen(true)] out HubMessage? message) =>
            inner.TryParseMessage(ref input, binder, out message);

        public void WriteMessage(HubMessage message, IBufferWriter<byte> output) => inner.WriteMessage(WithHeader(message), output);

        public ReadOnlyMemory<byte> GetMessageBytes(HubMessage message) => inner.GetMessageBytes(WithHeader(message));

        private HubMessage WithHeader(HubMessage message)
        {
            if (message is InvocationMessage invocation)
            {
                invocation.Headers ??= new Dictionary<string, string>();
                invocation.Headers["traceparent"] = traceparent;
            }

            return message;
        }
    }
}

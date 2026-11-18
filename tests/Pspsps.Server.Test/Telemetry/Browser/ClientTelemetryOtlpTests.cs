using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Pspsps.Server.Telemetry;
using Pspsps.Server.Telemetry.Browser;

namespace Pspsps.Server.Test.Telemetry.Browser;

public sealed class ClientTelemetryOtlpTests
{
    private static Dictionary<string, JsonElement> Attributes(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

    public static TelemetryOptions Telemetry()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OTEL_SERVICE_NAME"] = "pspsps",
            ["OTEL_RESOURCE_ATTRIBUTES"] = "deployment.environment.name=prod",
            ["HOSTNAME"] = "pspsps-abc",
        }).Build();
        var environment = Substitute.For<IHostEnvironment>();
        environment.ApplicationName.Returns("Pspsps.Server");
        environment.EnvironmentName.Returns("Production");
        return TelemetryOptions.FromConfiguration(configuration, environment, "1234567");
    }

    [Test]
    public void Attributes_KeepOpenTelemetryNamesAndPlainValues()
    {
        var clean = ClientAttributes.Clean(Attributes("""
            {"rpc.method":"Move","http.response.status_code":503,"ratio":0.5,"ok":true,
             "Bad Key":"x","service.name":"evil","nested":{"a":1},"list":[1],"nothing":null}
            """));

        Assert.That(clean, Is.EquivalentTo(new KeyValuePair<string, object>[]
        {
            new("rpc.method", "Move"),
            new("http.response.status_code", 503L),
            new("ratio", 0.5),
            new("ok", true),
        }));
    }

    [Test]
    public void Attributes_AreCappedInNumberAndLength_AndRedacted()
    {
        var many = Enumerable.Range(0, 40).ToDictionary(i => $"a{i}", i => JsonSerializer.SerializeToElement(i));
        var text = ClientAttributes.Clean(Attributes($$"""{"url.path":"/auth/callback?code=abc&page=2","long":"{{new string('x', 1500)}}"}"""));

        Assert.Multiple(() =>
        {
            Assert.That(ClientAttributes.Clean(many), Has.Count.EqualTo(ClientAttributes.MaxAttributes));
            Assert.That(text.Single(a => a.Key == "url.path").Value, Is.EqualTo("/auth/callback?code=REDACTED&page=2"));
            Assert.That((string)text.Single(a => a.Key == "long").Value, Has.Length.EqualTo(ClientAttributes.MaxValueLength));
        });
    }

    [Test]
    public void AStack_KeepsWhatTheBrowserSends()
    {
        var stack = new string('s', 4500);
        var clean = ClientAttributes.Clean(Attributes($$"""{"exception.stacktrace":"{{stack}}","page":"{{stack}}"}"""));

        Assert.Multiple(() =>
        {
            Assert.That((string)clean.Single(a => a.Key == "exception.stacktrace").Value, Has.Length.EqualTo(ClientAttributes.MaxStackLength));
            Assert.That((string)clean.Single(a => a.Key == "page").Value, Has.Length.EqualTo(ClientAttributes.MaxValueLength));
            Assert.That(ClientAttributes.MaxStackLength, Is.EqualTo(4000), "client/src/telemetry/errors.ts MAX_STACK");
        });
    }

    [TestCase("see /auth/callback?code=abc&page=2 then", "see /auth/callback?code=REDACTED&page=2 then")]
    [TestCase("wss://game.example/hubs/game?id=conn-token", "wss://game.example/hubs/game?id=REDACTED")]
    [TestCase("/x?width=3&userId=42&e%6Dail=a", "/x?width=3&userId=REDACTED&e%6Dail=REDACTED")]
    [TestCase("no query? here", "no query? here")]
    public void Redaction_RemovesSensitiveQueryValuesInText(string text, string expected) =>
        Assert.That(QueryRedaction.RedactIn(text), Is.EqualTo(expected));

    [Test]
    public void Nanos_AreExact() =>
        Assert.That(OtlpJson.Nanos(1_790_000_000_000.125), Is.EqualTo("1790000000000125000"));

    [Test]
    public void Traces_AreOtlpJson()
    {
        var span = ClientTelemetryValidationTests.Span() with
        {
            ParentSpanId = "1111111111111111",
            Status = "error",
            Attributes = Attributes("""{"rpc.method":"Move"}"""),
        };

        var json = OtlpJson.Traces(new Dictionary<string, string> { ["service.name"] = "pspsps" }, [span]);

        var otlp = json["resourceSpans"]![0]!;
        var sent = otlp["scopeSpans"]![0]!["spans"]![0]!;
        Assert.Multiple(() =>
        {
            Assert.That(otlp["resource"]!["attributes"]![0]!["key"]!.GetValue<string>(), Is.EqualTo("service.name"));
            Assert.That(otlp["scopeSpans"]![0]!["scope"]!["name"]!.GetValue<string>(), Is.EqualTo(OtlpJson.Scope));
            Assert.That(sent["traceId"]!.GetValue<string>(), Is.EqualTo(ClientTelemetryValidationTests.TraceId));
            Assert.That(sent["parentSpanId"]!.GetValue<string>(), Is.EqualTo("1111111111111111"));
            Assert.That(sent["kind"]!.GetValue<int>(), Is.EqualTo(3), "client");
            Assert.That(sent["status"]!["code"]!.GetValue<int>(), Is.EqualTo(2), "error");
            Assert.That(sent["endTimeUnixNano"]!.GetValue<string>(), Is.EqualTo("1790000000012500000"));
            Assert.That(sent["attributes"]![0]!["value"]!["stringValue"]!.GetValue<string>(), Is.EqualTo("Move"));
        });
    }

    [Test]
    public void Logs_AreOtlpJson_InTheirTrace_Redacted()
    {
        var json = OtlpJson.Logs(new Dictionary<string, string>(),
            [ClientTelemetryValidationTests.Log("GET /auth/callback?code=abc answered 503") with { Level = "error" }]);

        var record = json["resourceLogs"]![0]!["scopeLogs"]![0]!["logRecords"]![0]!;
        Assert.Multiple(() =>
        {
            Assert.That(record["severityNumber"]!.GetValue<int>(), Is.EqualTo(17));
            Assert.That(record["severityText"]!.GetValue<string>(), Is.EqualTo("ERROR"));
            Assert.That(record["body"]!["stringValue"]!.GetValue<string>(), Is.EqualTo("GET /auth/callback?code=REDACTED answered 503"));
            Assert.That(record["traceId"]!.GetValue<string>(), Is.EqualTo(ClientTelemetryValidationTests.TraceId));
            Assert.That(record["spanId"]!.GetValue<string>(), Is.EqualTo(ClientTelemetryValidationTests.SpanId));
        });
    }

    [Test]
    public void Resource_IsTheGamesAsTheBrowser_WithTheBrowsersBuildAndAgent()
    {
        var resource = new ClientTelemetryResource(Telemetry()).For("abc1234", "Mozilla/5.0");

        Assert.Multiple(() =>
        {
            Assert.That(resource["service.name"], Is.EqualTo("pspsps"));
            Assert.That(resource["deployment.environment.name"], Is.EqualTo("prod"));
            Assert.That(resource["service.component"], Is.EqualTo("browser"));
            Assert.That(resource["service.version"], Is.EqualTo("abc1234"), "the browser's build, not the pod's");
            Assert.That(resource["user_agent.original"], Is.EqualTo("Mozilla/5.0"));
            Assert.That(resource, Does.Not.ContainKey("service.instance.id"), "a browser is no pod");
        });
    }

    private static ClientTelemetryForwarder Forwarder(FakeCollector collector, ForwardFailures failures, RecordingLogger<ClientTelemetryForwarder>? logger = null) =>
        new(new HttpClient(collector), new ClientTelemetryResource(Telemetry()),
            Options.Create(new ClientTelemetryOptions { Enabled = true, OtlpEndpoint = "http://collector.test:4318/" }),
            failures, (ILogger<ClientTelemetryForwarder>?)logger ?? NullLogger<ClientTelemetryForwarder>.Instance);

    [Test]
    public async Task Forwarder_PostsSpansAndLogsToTheCollectorsHttpPaths()
    {
        using var collector = new FakeCollector();

        await Forwarder(collector, new ForwardFailures(TimeProvider.System))
            .ForwardAsync(ClientTelemetryValidationTests.Batch(), "Mozilla/5.0", CancellationToken.None);

        Assert.That(collector.Posts.Select(p => p.Path), Is.EqualTo(new[] { "/v1/traces", "/v1/logs" }));
        var resource = collector.Posts[0].Body["resourceSpans"]![0]!["resource"]!["attributes"]!.AsArray()
            .ToDictionary(a => a!["key"]!.GetValue<string>(), a => a!["value"]!["stringValue"]!.GetValue<string>());
        Assert.That(resource, Does.ContainKey("service.component").WithValue("browser"));
        Assert.That(resource, Does.ContainKey("service.version").WithValue("abc1234"));
    }

    [Test]
    public async Task Forwarder_SendsNothingForABatchOfOnlyNumbers()
    {
        using var collector = new FakeCollector();
        var batch = ClientTelemetryValidationTests.Batch() with { Spans = [], Logs = [] };

        await Forwarder(collector, new ForwardFailures(TimeProvider.System)).ForwardAsync(batch, "", CancellationToken.None);

        Assert.That(collector.Posts, Is.Empty);
    }

    [Test]
    public async Task Forwarder_WarnsOfFailuresAtMostOnceAMinute_CountingTheRest()
    {
        using var collector = new FakeCollector { Failure = new HttpRequestException("refused") };
        var clock = new FakeTimeProvider();
        var failures = new ForwardFailures(clock);
        var logger = new RecordingLogger<ClientTelemetryForwarder>();
        var forwarder = Forwarder(collector, failures, logger);
        var batch = ClientTelemetryValidationTests.Batch() with { Logs = [] };

        await forwarder.ForwardAsync(batch, "", CancellationToken.None);
        await forwarder.ForwardAsync(batch, "", CancellationToken.None);
        await forwarder.ForwardAsync(batch, "", CancellationToken.None);
        Assert.That(logger.Lines, Has.Count.EqualTo(1));

        clock.Advance(ForwardFailures.Interval);
        collector.Failure = null;
        collector.Status = System.Net.HttpStatusCode.BadRequest;
        await forwarder.ForwardAsync(batch, "", CancellationToken.None);

        Assert.That(logger.Lines, Has.Count.EqualTo(2));
        Assert.That(logger.Lines[1].Message, Does.Contain("refused the browser's traces (400); 2 more failures"));
    }

    [Test]
    public void Outbox_DropsBatchesPastItsCapacity_AndWarnsOnce()
    {
        var logger = new RecordingLogger<ClientTelemetryOutbox>();
        var outbox = new ClientTelemetryOutbox(logger, new ForwardFailures(new FakeTimeProvider()));
        var batch = ClientTelemetryValidationTests.Batch();

        var queued = Enumerable.Range(0, ClientTelemetryOutbox.Capacity + 5).Count(_ => outbox.Enqueue(batch, ""));

        Assert.That(queued, Is.EqualTo(ClientTelemetryOutbox.Capacity));
        Assert.That(logger.Lines, Has.Count.EqualTo(1));
        Assert.That(logger.Lines[0].Message, Does.Contain("full"));
    }

    [Test]
    public async Task Outbox_QueuesOnlyBatchesWithSomethingToForward_AndForwardsThem()
    {
        using var collector = new FakeCollector();
        var outbox = new ClientTelemetryOutbox(NullLogger<ClientTelemetryOutbox>.Instance, new ForwardFailures(TimeProvider.System));

        Assert.That(outbox.Enqueue(ClientTelemetryValidationTests.Batch() with { Spans = [], Logs = [] }, ""), Is.False);
        Assert.That(outbox.Enqueue(ClientTelemetryValidationTests.Batch() with { Logs = [] }, ""), Is.True);
        await outbox.ForwardQueuedAsync(Forwarder(collector, new ForwardFailures(TimeProvider.System)));

        Assert.That(collector.Posts.Select(p => p.Path), Is.EqualTo(new[] { "/v1/traces" }));
    }

    [Test]
    public async Task Outbox_ForwardsWhatIsQueued_InOnePostASignal()
    {
        using var collector = new FakeCollector();
        var outbox = new ClientTelemetryOutbox(NullLogger<ClientTelemetryOutbox>.Instance, new ForwardFailures(TimeProvider.System));
        for (var i = 0; i < 3; i++)
            outbox.Enqueue(ClientTelemetryValidationTests.Batch() with { Version = $"build{i}" }, "Mozilla/5.0");

        await outbox.ForwardQueuedAsync(Forwarder(collector, new ForwardFailures(TimeProvider.System)));

        Assert.That(collector.Posts.Select(p => p.Path), Is.EqualTo(new[] { "/v1/traces", "/v1/logs" }));
        Assert.That(collector.Posts[0].Body["resourceSpans"]!.AsArray(), Has.Count.EqualTo(3), "a resource each: each says its build");
        Assert.That(collector.Posts[1].Body["resourceLogs"]!.AsArray(), Has.Count.EqualTo(3));
    }

    [Test]
    public async Task Outbox_KeepsForwardingAfterABatchThatFails()
    {
        using var collector = new FakeCollector();
        var outbox = new ClientTelemetryOutbox(NullLogger<ClientTelemetryOutbox>.Instance, new ForwardFailures(TimeProvider.System));
        var good = Forwarder(collector, new ForwardFailures(TimeProvider.System));
        var calls = 0;
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stop = new CancellationTokenSource();
        outbox.Enqueue(ClientTelemetryValidationTests.Batch(), "");

        var pump = outbox.ForwardAsync(() =>
        {
            if (++calls > 1)
                return good;
            failed.SetResult();
            throw new InvalidOperationException("broken");
        }, stop.Token);
        await failed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        outbox.Enqueue(ClientTelemetryValidationTests.Batch(), "");
        var post = await collector.NextAsync();
        await stop.CancelAsync();

        Assert.That(post.Path, Is.EqualTo("/v1/traces"));
        Assert.CatchAsync<OperationCanceledException>(async () => await pump);
    }
}

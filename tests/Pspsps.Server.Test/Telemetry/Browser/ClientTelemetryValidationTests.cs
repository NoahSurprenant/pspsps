using Pspsps.Server.Telemetry.Browser;

namespace Pspsps.Server.Test.Telemetry.Browser;

public sealed class ClientTelemetryValidationTests
{
    public const string TraceId = "0af7651916cd43dd8448eb211c80319c";
    public const string SpanId = "b7ad6b7169203331";
    public const double Now = 1_790_000_000_000;

    public static ClientSpan Span(string name = "GameHub/Move") => new()
    {
        TraceId = TraceId, SpanId = SpanId, Name = name, Kind = "client", Start = Now, End = Now + 12.5, Status = "unset",
    };

    public static ClientLog Log(string message = "The hub connection is up") => new()
    {
        Time = Now, Level = "info", Message = message, TraceId = TraceId, SpanId = SpanId,
    };

    public static ClientTelemetryBatch Batch() => new()
    {
        Version = "abc1234",
        Spans = [Span()],
        Logs = [Log()],
        Vitals = [new ClientVital { Name = "LCP", Value = 1200 }, new ClientVital { Name = "FPS", Value = 59.8 }],
    };

    [Test]
    public void ABatchTheBrowserSends_IsValid() =>
        Assert.That(ClientTelemetryValidation.Problem(Batch()), Is.Null);

    [Test]
    public void AnEmptyBatch_IsValid() =>
        Assert.That(ClientTelemetryValidation.Problem(new ClientTelemetryBatch()), Is.Null);

    [Test]
    public void TooManyOfAnything_IsRefused()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ClientTelemetryValidation.Problem(Batch() with { Spans = Enumerable.Repeat(Span(), 101).ToList() }), Is.Not.Null);
            Assert.That(ClientTelemetryValidation.Problem(Batch() with { Logs = Enumerable.Repeat(Log(), 101).ToList() }), Is.Not.Null);
            Assert.That(ClientTelemetryValidation.Problem(Batch() with
            {
                Vitals = Enumerable.Repeat(new ClientVital { Name = "FPS", Value = 60 }, 21).ToList(),
            }), Is.Not.Null);
            Assert.That(ClientTelemetryValidation.Problem(Batch() with { Spans = Enumerable.Repeat(Span(), 100).ToList() }), Is.Null);
        });
    }

    [TestCase("ABC1234", true)]
    [TestCase("v1.2-rc_3", true)]
    [TestCase("two words", false)]
    [TestCase("", false)]
    public void TheVersion_IsAWordOfLettersDigitsDotsAndDashes(string version, bool valid) =>
        Assert.That(ClientTelemetryValidation.Problem(Batch() with { Version = version }) is null, Is.EqualTo(valid));

    [TestCase("0AF7651916CD43DD8448EB211C80319C", Description = "upper case")]
    [TestCase("00000000000000000000000000000000", Description = "all zeros")]
    [TestCase("0af7651916cd43dd", Description = "too short")]
    [TestCase("", Description = "empty")]
    public void ABadTraceId_IsRefused(string traceId)
    {
        Assert.Multiple(() =>
        {
            Assert.That(ClientTelemetryValidation.Problem(Batch() with { Spans = [Span() with { TraceId = traceId }] }), Is.Not.Null);
            Assert.That(ClientTelemetryValidation.Problem(Batch() with { Logs = [Log() with { TraceId = traceId }] }), Is.Not.Null);
        });
    }

    [Test]
    public void ABadSpan_IsRefused()
    {
        ClientSpan[] bad =
        [
            Span() with { SpanId = "0000000000000000" },
            Span() with { ParentSpanId = "xyz" },
            Span() with { Name = "" },
            Span() with { Name = new string('a', 201) },
            Span() with { Kind = "server" },
            Span() with { Status = "fine" },
            Span() with { Start = 0 },
            Span() with { End = 1e14 },
        ];
        foreach (var span in bad)
            Assert.That(ClientTelemetryValidation.Problem(Batch() with { Spans = [span] }), Is.Not.Null, span.ToString());
    }

    [Test]
    public void ABadLog_IsRefused()
    {
        ClientLog[] bad =
        [
            Log() with { Level = "fatal" },
            Log() with { Message = "" },
            Log() with { Message = new string('a', 4001) },
            Log() with { SpanId = null },
            Log() with { Time = double.NaN },
        ];
        foreach (var log in bad)
            Assert.That(ClientTelemetryValidation.Problem(Batch() with { Logs = [log] }), Is.Not.Null, log.ToString());
        Assert.That(ClientTelemetryValidation.Problem(Batch() with { Logs = [Log() with { TraceId = null, SpanId = null }] }), Is.Null,
            "a line in no trace");
    }

    [TestCase("LCP", -1)]
    [TestCase("LCP", 600_001)]
    [TestCase("HEAP", 1)]
    public void ABadNumber_IsRefused(string name, double value) =>
        Assert.That(ClientTelemetryValidation.Problem(Batch() with { Vitals = [new ClientVital { Name = name, Value = value }] }), Is.Not.Null);

    [Test]
    public void NullLists_AreRefused() =>
        Assert.That(ClientTelemetryValidation.Problem(Batch() with { Spans = null! }), Is.Not.Null);
}

using System.Text.Json;
using System.Text.RegularExpressions;

namespace Pspsps.Server.Telemetry.Browser;

/// <summary>
/// One batch from a browser (client/src/telemetry/telemetry.ts): what it collected since its last, and the build it runs.
/// Every list and text is bounded (<see cref="ClientTelemetryLimits"/>); <see cref="ClientTelemetryValidation"/> checks it.
/// </summary>
public sealed record ClientTelemetryBatch
{
    /// <summary>The client build's version (its commit), for service.version.</summary>
    public string? Version { get; init; }

    public IReadOnlyList<ClientSpan> Spans { get; init; } = [];

    public IReadOnlyList<ClientLog> Logs { get; init; } = [];

    public IReadOnlyList<ClientVital> Vitals { get; init; } = [];
}

/// <summary>A finished span. Times are the browser's clock, in epoch milliseconds.</summary>
public sealed record ClientSpan
{
    public string TraceId { get; init; } = "";

    public string SpanId { get; init; } = "";

    public string? ParentSpanId { get; init; }

    public string Name { get; init; } = "";

    /// <summary><c>internal</c> or <c>client</c> (a call to the server, a hub call).</summary>
    public string Kind { get; init; } = "internal";

    public double Start { get; init; }

    public double End { get; init; }

    /// <summary><c>unset</c>, <c>ok</c> or <c>error</c>.</summary>
    public string Status { get; init; } = "unset";

    public IReadOnlyDictionary<string, JsonElement>? Attributes { get; init; }
}

/// <summary>A log line, in the trace it names (if any).</summary>
public sealed record ClientLog
{
    public double Time { get; init; }

    /// <summary><c>debug</c>, <c>info</c>, <c>warn</c> or <c>error</c>.</summary>
    public string Level { get; init; } = "";

    public string Message { get; init; } = "";

    public string? TraceId { get; init; }

    public string? SpanId { get; init; }

    public IReadOnlyDictionary<string, JsonElement>? Attributes { get; init; }
}

/// <summary>A number: a Web Vital (milliseconds; CLS unitless), the frame rate in a room, or a long task (milliseconds).</summary>
public sealed record ClientVital
{
    public string Name { get; init; } = "";

    public double Value { get; init; }
}

public static class ClientTelemetryLimits
{
    public const int Spans = 100;
    public const int Logs = 100;
    public const int Vitals = 20;
    public const int BodyBytes = 256 * 1024;
    public const int SpanName = 200;
    public const int LogMessage = 4000;
    public const double MaxVital = 600_000;
    /// <summary>
    /// 2001 and 2255 in epoch milliseconds: anything else is no browser's clock. Below the last time OTLP's nanoseconds
    /// (a long) can hold, in 2262.
    /// </summary>
    public const double MinEpochMs = 1e12;
    public const double MaxEpochMs = 9e12;
}

/// <summary>Whether a browser's batch is one the browser could have sent: anything else is refused whole (a 400).</summary>
public static partial class ClientTelemetryValidation
{
    public static readonly IReadOnlySet<string> VitalNames =
        new HashSet<string>(StringComparer.Ordinal) { "LCP", "INP", "CLS", "FCP", "TTFB", "FPS", "LONG_TASK" };

    private static readonly HashSet<string> Kinds = new(StringComparer.Ordinal) { "internal", "client" };
    private static readonly HashSet<string> Statuses = new(StringComparer.Ordinal) { "unset", "ok", "error" };
    private static readonly HashSet<string> Levels = new(StringComparer.Ordinal) { "debug", "info", "warn", "error" };

    /// <summary>What is wrong with <paramref name="batch"/>, or null when nothing is.</summary>
    public static string? Problem(ClientTelemetryBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (batch.Spans is null || batch.Logs is null || batch.Vitals is null)
            return "spans, logs and vitals must be lists";
        if (batch.Spans.Count > ClientTelemetryLimits.Spans || batch.Logs.Count > ClientTelemetryLimits.Logs
            || batch.Vitals.Count > ClientTelemetryLimits.Vitals)
            return "too many items";
        if (batch.Version is not null && !Version().IsMatch(batch.Version))
            return "version";
        foreach (var span in batch.Spans)
        {
            if (span is null || !IsTraceId(span.TraceId) || !IsSpanId(span.SpanId)
                || (span.ParentSpanId is not null && !IsSpanId(span.ParentSpanId))
                || string.IsNullOrEmpty(span.Name) || span.Name.Length > ClientTelemetryLimits.SpanName
                || !Kinds.Contains(span.Kind ?? "") || !Statuses.Contains(span.Status ?? "")
                || !IsTime(span.Start) || !IsTime(span.End))
                return "span";
        }

        foreach (var log in batch.Logs)
        {
            if (log is null || !IsTime(log.Time) || !Levels.Contains(log.Level ?? "")
                || string.IsNullOrEmpty(log.Message) || log.Message.Length > ClientTelemetryLimits.LogMessage
                || (log.TraceId is null) != (log.SpanId is null)
                || (log.TraceId is not null && !IsTraceId(log.TraceId)) || (log.SpanId is not null && !IsSpanId(log.SpanId)))
                return "log";
        }

        foreach (var vital in batch.Vitals)
        {
            if (vital is null || !VitalNames.Contains(vital.Name ?? "") || !double.IsFinite(vital.Value)
                || vital.Value < 0 || vital.Value > ClientTelemetryLimits.MaxVital)
                return "vital";
        }

        return null;
    }

    /// <summary>32 lower-case hex digits, not all zero (W3C calls that invalid).</summary>
    public static bool IsTraceId(string? value) => value is not null && TraceId().IsMatch(value) && value.Any(c => c != '0');

    /// <summary>16 lower-case hex digits, not all zero.</summary>
    public static bool IsSpanId(string? value) => value is not null && SpanId().IsMatch(value) && value.Any(c => c != '0');

    private static bool IsTime(double epochMs) =>
        double.IsFinite(epochMs) && epochMs >= ClientTelemetryLimits.MinEpochMs && epochMs <= ClientTelemetryLimits.MaxEpochMs;

    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex TraceId();

    [GeneratedRegex("^[0-9a-f]{16}$")]
    private static partial Regex SpanId();

    /// <summary>A commit or a version: what client/src/telemetry/buildVersion.ts makes.</summary>
    [GeneratedRegex("^[0-9A-Za-z._-]{1,40}$")]
    private static partial Regex Version();
}

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using OpenTelemetry;

namespace Pspsps.Server.Telemetry.Browser;

/// <summary>
/// The resource the browser's spans and logs go under: the game's (its service.name, environment and attributes), as
/// <c>service.component=browser</c>, without this pod's instance ID or version (a browser is no pod, and runs its own
/// build: each batch says which, <see cref="For"/>).
/// </summary>
public sealed class ClientTelemetryResource(TelemetryOptions telemetry)
{
    public const string Component = "browser";

    private readonly Dictionary<string, string> _attributes = Build(telemetry);

    /// <summary>The resource for one batch: its build's service.version and the browser's user agent.</summary>
    public Dictionary<string, string> For(string? version, string? userAgent)
    {
        var attributes = new Dictionary<string, string>(_attributes, StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(version))
            attributes["service.version"] = version;
        if (!string.IsNullOrWhiteSpace(userAgent))
            attributes["user_agent.original"] = ClientAttributes.Bounded(userAgent);
        return attributes;
    }

    private static Dictionary<string, string> Build(TelemetryOptions telemetry)
    {
        var attributes = telemetry.ResourceAttributes
            .Where(a => a.Key is not "service.instance.id" and not "service.version")
            .ToDictionary(a => a.Key, a => Convert.ToString(a.Value, CultureInfo.InvariantCulture) ?? "", StringComparer.Ordinal);
        attributes["service.component"] = Component;
        return attributes;
    }
}

/// <summary>
/// The browser's attributes as they may be recorded: at most <see cref="MaxAttributes"/>, named like OpenTelemetry's
/// (lower case, dots), strings bounded and their URLs' sensitive query values redacted, numbers and booleans as they are.
/// Nothing may claim a resource's name (service.*).
/// </summary>
public static partial class ClientAttributes
{
    public const int MaxAttributes = 32;
    public const int MaxValueLength = 1000;

    public static List<KeyValuePair<string, object>> Clean(IReadOnlyDictionary<string, JsonElement>? attributes)
    {
        var clean = new List<KeyValuePair<string, object>>();
        if (attributes is null)
            return clean;
        foreach (var (key, value) in attributes)
        {
            if (clean.Count == MaxAttributes)
                break;
            if (!Key().IsMatch(key) || key.StartsWith("service.", StringComparison.Ordinal))
                continue;
            object? kept = value.ValueKind switch
            {
                JsonValueKind.String => QueryRedaction.RedactIn(Bounded(value.GetString() ?? "")),
                JsonValueKind.Number when value.TryGetInt64(out var whole) => whole,
                JsonValueKind.Number => value.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            };
            if (kept is not null)
                clean.Add(new(key, kept));
        }

        return clean;
    }

    /// <summary>At most <see cref="MaxValueLength"/> characters.</summary>
    public static string Bounded(string value) => value.Length > MaxValueLength ? value[..MaxValueLength] : value;

    [GeneratedRegex("^[a-z][a-z0-9_.]{0,63}$")]
    private static partial Regex Key();
}

/// <summary>
/// The values of sensitive query parameters as REDACTED, as the browser does (client/src/telemetry/redact.ts), again
/// here: tokens, codes, keys, email, user, state, SignalR's connection token (a parameter named just <c>id</c>)...
/// </summary>
public static partial class QueryRedaction
{
    public const string Redacted = "REDACTED";

    /// <summary>One query string (from its "?"), its sensitive values redacted.</summary>
    public static string RedactQuery(string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var question = query.IndexOf('?', StringComparison.Ordinal);
        if (question < 0)
            return query;
        var pairs = query[(question + 1)..].Split('&').Select(pair =>
        {
            var equals = pair.IndexOf('=', StringComparison.Ordinal);
            if (equals < 0)
                return pair;
            var name = pair[..equals];
            return IsSensitive(Unescape(name)) ? $"{name}={Redacted}" : pair;
        });
        return query[..(question + 1)] + string.Join('&', pairs);
    }

    /// <summary>
    /// <paramref name="text"/> (a log's message, a stack, a path) with every query string in it redacted, whatever the URL
    /// around it: "see /auth/callback?code=abc&amp;page=2" becomes "see /auth/callback?code=REDACTED&amp;page=2".
    /// </summary>
    public static string RedactIn(string text) => QueryInText().Replace(text, match => RedactQuery(match.Value));

    public static bool IsSensitive(string name) => Sensitive().IsMatch(name);

    private static string Unescape(string name)
    {
        try
        {
            return Uri.UnescapeDataString(name);
        }
        catch (UriFormatException)
        {
            return name;
        }
    }

    [GeneratedRegex("token|code|secret|passw|pwd|key|auth|session|sig|credential|cookie|email|user|state|nonce|otp|ticket|hash|jwt|^id$", RegexOptions.IgnoreCase)]
    private static partial Regex Sensitive();

    /// <summary>A query string in text: a "?" and what follows up to a space, quote, bracket or "#".</summary>
    [GeneratedRegex("""\?[^\s#'"<>()]+""")]
    private static partial Regex QueryInText();
}

/// <summary>OTLP/JSON (what a collector's /v1/traces and /v1/logs take) for a browser's batch.</summary>
public static class OtlpJson
{
    public const string Scope = "pspsps.browser";

    private static readonly Dictionary<string, (int Number, string Text)> Severities = new(StringComparer.Ordinal)
    {
        ["debug"] = (5, "DEBUG"),
        ["info"] = (9, "INFO"),
        ["warn"] = (13, "WARN"),
        ["error"] = (17, "ERROR"),
    };

    public static JsonObject Traces(IReadOnlyDictionary<string, string> resource, IEnumerable<ClientSpan> spans) => new()
    {
        ["resourceSpans"] = new JsonArray(new JsonObject
        {
            ["resource"] = Resource(resource),
            ["scopeSpans"] = new JsonArray(new JsonObject
            {
                ["scope"] = new JsonObject { ["name"] = Scope },
                ["spans"] = new JsonArray(spans.Select(Span).ToArray<JsonNode?>()),
            }),
        }),
    };

    public static JsonObject Logs(IReadOnlyDictionary<string, string> resource, IEnumerable<ClientLog> logs) => new()
    {
        ["resourceLogs"] = new JsonArray(new JsonObject
        {
            ["resource"] = Resource(resource),
            ["scopeLogs"] = new JsonArray(new JsonObject
            {
                ["scope"] = new JsonObject { ["name"] = Scope },
                ["logRecords"] = new JsonArray(logs.Select(Log).ToArray<JsonNode?>()),
            }),
        }),
    };

    private static JsonObject Span(ClientSpan span)
    {
        var json = new JsonObject
        {
            ["traceId"] = span.TraceId,
            ["spanId"] = span.SpanId,
            ["name"] = QueryRedaction.RedactIn(span.Name),
            // SPAN_KIND_INTERNAL, SPAN_KIND_CLIENT
            ["kind"] = span.Kind == "client" ? 3 : 1,
            ["startTimeUnixNano"] = Nanos(span.Start),
            ["endTimeUnixNano"] = Nanos(Math.Max(span.Start, span.End)),
            ["attributes"] = Attributes(ClientAttributes.Clean(span.Attributes)),
            // STATUS_CODE_UNSET, OK, ERROR
            ["status"] = new JsonObject { ["code"] = span.Status switch { "ok" => 1, "error" => 2, _ => 0 } },
        };
        if (span.ParentSpanId is not null)
            json["parentSpanId"] = span.ParentSpanId;
        return json;
    }

    private static JsonObject Log(ClientLog log)
    {
        var (number, text) = Severities[log.Level];
        var json = new JsonObject
        {
            ["timeUnixNano"] = Nanos(log.Time),
            ["severityNumber"] = number,
            ["severityText"] = text,
            ["body"] = new JsonObject { ["stringValue"] = QueryRedaction.RedactIn(log.Message) },
            ["attributes"] = Attributes(ClientAttributes.Clean(log.Attributes)),
        };
        if (log.TraceId is not null && log.SpanId is not null)
        {
            json["traceId"] = log.TraceId;
            json["spanId"] = log.SpanId;
        }

        return json;
    }

    private static JsonObject Resource(IReadOnlyDictionary<string, string> resource) => new()
    {
        ["attributes"] = Attributes(resource.Select(a => new KeyValuePair<string, object>(a.Key, a.Value))),
    };

    private static JsonArray Attributes(IEnumerable<KeyValuePair<string, object>> attributes) =>
        new(attributes.Select(a => (JsonNode?)new JsonObject { ["key"] = a.Key, ["value"] = Value(a.Value) }).ToArray());

    private static JsonObject Value(object value) => value switch
    {
        bool b => new JsonObject { ["boolValue"] = b },
        // OTLP/JSON writes 64-bit integers as strings.
        long l => new JsonObject { ["intValue"] = l.ToString(CultureInfo.InvariantCulture) },
        double d => new JsonObject { ["doubleValue"] = d },
        _ => new JsonObject { ["stringValue"] = Convert.ToString(value, CultureInfo.InvariantCulture) },
    };

    /// <summary>
    /// Epoch milliseconds (with a fraction) as OTLP's nanoseconds, a string. In integers: the product as a double (about
    /// 1.8e18, past 2^53) would be off by tens of nanoseconds.
    /// </summary>
    public static string Nanos(double epochMs)
    {
        var whole = Math.Floor(epochMs);
        var nanos = ((long)whole * 1_000_000) + (long)Math.Round((epochMs - whole) * 1_000_000);
        return nanos.ToString(CultureInfo.InvariantCulture);
    }
}

/// <summary>Sends a browser's spans and logs on to the collector (ClientTelemetry:OtlpEndpoint), OTLP/JSON over HTTP.</summary>
public sealed partial class ClientTelemetryForwarder(
    HttpClient http,
    ClientTelemetryResource resource,
    IOptions<ClientTelemetryOptions> options,
    ForwardFailures failures,
    ILogger<ClientTelemetryForwarder> logger)
{
    /// <summary>How long one post to the collector may take.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public async Task ForwardAsync(ClientTelemetryBatch batch, string userAgent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var endpoint = options.Value.OtlpUri;
        if (endpoint is null || (batch.Spans.Count == 0 && batch.Logs.Count == 0))
            return;

        var attributes = resource.For(batch.Version, userAgent);
        // Not this post's own HttpClient span and log: every batch would make one.
        using var suppressed = SuppressInstrumentationScope.Begin();
        if (batch.Spans.Count > 0)
            await PostAsync(endpoint, "traces", OtlpJson.Traces(attributes, batch.Spans), ct);
        if (batch.Logs.Count > 0)
            await PostAsync(endpoint, "logs", OtlpJson.Logs(attributes, batch.Logs), ct);
    }

    private async Task PostAsync(Uri endpoint, string signal, JsonObject body, CancellationToken ct)
    {
        var target = new Uri(endpoint.AbsoluteUri.TrimEnd('/') + "/v1/" + signal);
        try
        {
            using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            using var response = await http.PostAsync(target, content, ct);
            if (!response.IsSuccessStatusCode && failures.ShouldLog(out var suppressed))
                LogRefused(logger, signal, (int)response.StatusCode, suppressed);
        }
        catch (Exception e) when (e is HttpRequestException || (e is TaskCanceledException && !ct.IsCancellationRequested))
        {
            // Telemetry is best effort: the browser's batch is dropped.
            if (failures.ShouldLog(out var suppressed))
                LogFailed(logger, e, signal, suppressed);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The collector refused the browser's {Signal} ({Status}); {Suppressed} more failures since the last warning")]
    private static partial void LogRefused(ILogger logger, string signal, int status, int suppressed);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sending the browser's {Signal} to the collector failed; {Suppressed} more failures since the last warning")]
    private static partial void LogFailed(ILogger logger, Exception e, string signal, int suppressed);
}

/// <summary>
/// The forwarding failures, warned of at most once a minute: in a collector outage every player's batch fails, and a
/// warning each would flood the log. The next warning counts the ones left out.
/// </summary>
public sealed class ForwardFailures(TimeProvider time)
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly Lock _lock = new();
    private DateTimeOffset? _lastWarned;
    private int _suppressed;

    /// <summary>Whether this failure is warned of; <paramref name="suppressed"/>: how many weren't since the last.</summary>
    public bool ShouldLog(out int suppressed)
    {
        lock (_lock)
        {
            var now = time.GetUtcNow();
            if (_lastWarned is { } last && now - last < Interval)
            {
                _suppressed++;
                suppressed = 0;
                return false;
            }

            _lastWarned = now;
            suppressed = _suppressed;
            _suppressed = 0;
            return true;
        }
    }
}

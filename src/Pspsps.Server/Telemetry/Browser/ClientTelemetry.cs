using System.Globalization;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Pspsps.Server.Auth;

namespace Pspsps.Server.Telemetry.Browser;

/// <summary>
/// The browser's telemetry (client/src/telemetry): its spans and log lines, passed on to the collector as the game's
/// (service.component=browser), and its numbers, recorded as the game's metrics. Off unless <c>ClientTelemetry:Enabled</c>
/// (a 404 then, and the browser stops sending). docs/telemetry.md, "From the browser".
/// </summary>
public sealed class ClientTelemetryOptions
{
    public const string Section = "ClientTelemetry";

    public bool Enabled { get; set; }

    /// <summary>The collector's OTLP/HTTP base, e.g. <c>http://otel-collector:4318</c>: required when enabled.</summary>
    public string? OtlpEndpoint { get; set; }

    /// <summary>Batches each player (or, signed out, each client address) may send a minute, on each pod; the rest are a 429.</summary>
    public int PermitsPerMinute { get; set; } = 60;

    /// <summary>The OTLP endpoint as a URI; null when it isn't an http(s) URL.</summary>
    public Uri? OtlpUri => TelemetryOptions.ParseEndpoint(OtlpEndpoint);
}

public static class ClientTelemetry
{
    public const string Path = "/api/client-telemetry";
    public const string RateLimitPolicy = "client-telemetry";

    /// <summary>
    /// The Server-Timing entry on the game's page when the server takes the browser's telemetry: the browser reads it
    /// from the navigation's timing (no request of its own) and records nothing without it. Its description is the share
    /// of hub calls the browser traces: the server's own share of root spans, so hub calls are traced as often as before.
    /// </summary>
    public const string MarkName = "pspsps-telemetry";

    public const string ServerTimingHeader = "Server-Timing";

    /// <summary>
    /// <see cref="ClientTelemetryOptions"/> (checked at startup: enabled takes an OTLP endpoint), the forwarder, the
    /// browsers' meter, and the rate limit: a fixed window per player, or per client address when signed out.
    /// </summary>
    public static IServiceCollection AddClientTelemetry(this IServiceCollection services, IConfiguration configuration, TelemetryOptions telemetry)
    {
        services.AddOptions<ClientTelemetryOptions>().Bind(configuration.GetSection(ClientTelemetryOptions.Section))
            .Validate(o => !o.Enabled || o.OtlpUri is not null,
                "ClientTelemetry:OtlpEndpoint must be an http(s) URL when ClientTelemetry:Enabled is true")
            .Validate(o => o.PermitsPerMinute is >= 1 and <= 1000, "ClientTelemetry:PermitsPerMinute must be from 1 to 1000")
            .ValidateOnStart();
        services.AddSingleton(new ClientTelemetryResource(telemetry));
        services.AddSingleton<BrowserMetrics>();
        services.AddSingleton<ForwardFailures>();
        services.AddSingleton<ClientTelemetryOutbox>();
        services.AddHttpClient<ClientTelemetryForwarder>(client => client.Timeout = ClientTelemetryForwarder.Timeout);
        services.AddHostedService<ClientTelemetryPump>();
        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddPolicy(RateLimitPolicy, context => PartitionFor(context,
                context.RequestServices.GetRequiredService<IOptions<ClientTelemetryOptions>>().Value.PermitsPerMinute));
        });
        return services;
    }

    /// <summary>
    /// A window of <paramref name="permitsPerMinute"/> batches a minute for the player signed in (guests are players,
    /// made by anyone, so a player isn't trusted more), or for the request's client address when signed out. Needs
    /// authentication to have run first (UseRateLimiter after UseAuthentication).
    /// </summary>
    public static RateLimitPartition<string> PartitionFor(HttpContext context, int permitsPerMinute)
    {
        ArgumentNullException.ThrowIfNull(context);
        var key = PlayerClaims.GetPlayerId(context.User) is { } player
            ? "player:" + player.ToString()
            : "address:" + context.Connection.RemoteIpAddress;
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, permitsPerMinute),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
    }

    /// <summary>POST /api/client-telemetry: anonymous, from the game's own pages only, rate-limited and size-limited.</summary>
    public static IEndpointConventionBuilder MapClientTelemetry(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost(Path, ReportAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicy)
            .WithMetadata(new RequestSizeLimitAttribute(ClientTelemetryLimits.BodyBytes));

    /// <summary>
    /// Takes a batch of a browser's telemetry: its numbers become the game's metrics, its spans and log lines are queued
    /// for the collector (<see cref="ClientTelemetryPump"/>), so the browser never waits on it. A 404 when it is off, a
    /// 403 from another site, a 400 for a batch the browser couldn't have sent.
    /// </summary>
    public static async Task<IResult> ReportAsync(HttpContext http, IOptions<ClientTelemetryOptions> options,
        ClientTelemetryOutbox outbox, BrowserMetrics metrics, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(metrics);
        if (!options.Value.Enabled)
            return Results.NotFound();
        // Anonymous, so it checks where the request came from: another site can't fill the game's telemetry.
        if (!SameOriginRequest.IsFromThisSite(http.Request))
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (http.Request.ContentLength > ClientTelemetryLimits.BodyBytes)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        if (!http.Request.HasJsonContentType())
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);

        ClientTelemetryBatch? batch;
        try
        {
            batch = await http.Request.ReadFromJsonAsync<ClientTelemetryBatch>(ct);
        }
        catch (JsonException)
        {
            return Results.BadRequest();
        }

        if (batch is null || ClientTelemetryValidation.Problem(batch) is not null)
            return Results.BadRequest();

        metrics.Record(batch.Vitals);
        outbox.Enqueue(batch, http.Request.Headers.UserAgent.ToString());
        return Results.NoContent();
    }

    /// <summary>The browsers' batches: every few seconds a tab, so left out of the traces and the request log.</summary>
    public static bool IsTelemetryPath(PathString path) =>
        path.StartsWithSegments(Path, StringComparison.OrdinalIgnoreCase);

    /// <summary><c>pspsps-telemetry;desc="0.1"</c>: the mark, with the share of hub calls to trace.</summary>
    public static string Mark(double sampleRatio) =>
        $"{MarkName};desc=\"{Math.Clamp(sampleRatio, 0, 1).ToString("0.####", CultureInfo.InvariantCulture)}\"";

    /// <summary>
    /// Marks the game's page (Server-Timing, <see cref="MarkName"/>) when browser telemetry is on: an HTML response, or
    /// any answer to the browser loading a page (a 304 for index.html has no content type). Nothing when it is off.
    /// </summary>
    public static IApplicationBuilder UseClientTelemetryMark(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var options = app.ApplicationServices.GetRequiredService<IOptions<ClientTelemetryOptions>>().Value;
        if (!options.Enabled)
            return app;
        var mark = Mark(MetricsAndTracingExtensions.RootSampleRatio(app.ApplicationServices.GetRequiredService<IConfiguration>()));
        return app.Use(async (context, next) =>
        {
            if (HttpMethods.IsGet(context.Request.Method))
            {
                context.Response.OnStarting(() =>
                {
                    if (IsPage(context))
                        context.Response.Headers.Append(ServerTimingHeader, mark);
                    return Task.CompletedTask;
                });
            }

            await next(context);
        });
    }

    private static bool IsPage(HttpContext context) =>
        context.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true
        || string.Equals(context.Request.Headers["Sec-Fetch-Dest"], "document", StringComparison.Ordinal);
}

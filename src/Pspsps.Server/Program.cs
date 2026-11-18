using System.Text.Json.Serialization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Pspsps.Application;
using Pspsps.Infrastructure;
using Pspsps.Infrastructure.Persistence;
using Pspsps.Server;
using Pspsps.Server.Auth;
using Pspsps.Server.Game;
using Pspsps.Server.Hubs;
using Pspsps.Server.Moderation;
using Pspsps.Server.Telemetry;
using Pspsps.Server.Telemetry.Browser;
using Serilog;
using StackExchange.Redis;

Log.Logger = LoggingExtensions.CreateBootstrapLogger();
try
{
    var builder = WebApplication.CreateBuilder(args);

    // Logs, metrics and traces share one resource and go to OTEL_EXPORTER_OTLP_ENDPOINT when it's set
    // (docs/telemetry.md).
    var telemetry = TelemetryOptions.FromConfiguration(
        builder.Configuration, builder.Environment, TelemetryOptions.ServiceVersionOf(typeof(Program).Assembly));
    builder.AddSerilogLogging(telemetry);
    builder.Services.AddMetricsAndTracing(telemetry, builder.Configuration);
    builder.Services.AddSingleton<GameMetrics>();
    // The browsers' telemetry, passed on to the collector when ClientTelemetry:Enabled (docs/telemetry.md).
    builder.Services.AddClientTelemetry(builder.Configuration, telemetry);

    var database = builder.Configuration.GetConnectionString("Pspsps")
        ?? throw new InvalidOperationException("Set ConnectionStrings:Pspsps to the PostgreSQL database.");

    // Redis joins the pods into one game: Orleans' membership table and the SignalR backplane. Without it the server
    // is a single local silo, which is all development and the tests need.
    var redis = builder.Configuration.GetConnectionString("Redis");

    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(database);
    var providers = builder.AddGameAuthentication();

    builder.Services.ConfigureHttpJsonOptions(options =>
        options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
    // Admins (by player ID, from Moderation:AdminPlayerIds), and what keeps a banned player off the hub.
    builder.Services.AddModeration(builder.Configuration);
    var signalR = builder.Services.AddSignalR(options =>
        {
            options.AddFilter<HubCallMetricsFilter>();
            options.AddFilter<ModerationHubFilter>();
        })
        .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
    if (!string.IsNullOrEmpty(redis))
    {
        signalR.AddStackExchangeRedis(redis, options =>
            options.Configuration.ChannelPrefix = RedisChannel.Literal("pspsps"));
    }

    // A connection's user is its player, for messages to all of a player's tabs (friend requests).
    builder.Services.AddSingleton<IUserIdProvider, PlayerUserIdProvider>();
    builder.Services.AddSingleton<IRoomBroadcaster, HubRoomBroadcaster>();
    // The words chat masks or refuses, from chat-filter/chat-filter.json, reread when it changes.
    builder.AddChatFilter();
    // Renews this pod's connections' leases with their rooms and worlds; a dead pod's cats leave when theirs run out.
    builder.Services.AddSingleton<SessionHeartbeat>();
    builder.Services.AddHostedService(services => services.GetRequiredService<SessionHeartbeat>());

    builder.UseOrleans(silo =>
    {
        // A hub call's trace follows it into the grains it calls, on whichever pod they live.
        silo.AddActivityPropagation();
        if (string.IsNullOrEmpty(redis))
        {
            silo.UseLocalhostClustering();
            return;
        }

        silo.UseRedisClustering(redis);
        // In Kubernetes: silo name and address from the pod, IDs from its orleans/* labels, dead pods' silos removed.
        if (!string.IsNullOrEmpty(builder.Configuration["POD_NAME"]))
        {
            silo.UseKubernetesHosting();
        }
    });

    builder.Services.AddAppHealthChecks(builder.Configuration);
    // Who may set the client address and scheme (ForwardedHeaders:*): nobody but loopback unless configured.
    (await builder.Services.ConfigureAppForwardedHeadersAsync(builder.Configuration)).LogTo(Log.Logger);

    var app = builder.Build();

    // Build the chat filter now, so a missing or broken word list is logged at startup, not at the first line said.
    app.Services.GetRequiredService<ChatFilters>();

    await using (var scope = app.Services.CreateAsyncScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<PspspsDbContext>();
        var time = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        // Waits for the database first (DatabaseStartup says why, and how it fits the chart's startup probe).
        var wait = DatabaseStartup.OptionsFrom(app.Configuration, invalid => Log.Warning(
            "{Key} is {Value}, not a whole number from 1 to {Max}; waiting the default {Seconds}s",
            DatabaseStartup.BudgetKey, invalid,DatabaseStartup.MaxBudgetSeconds, DatabaseStartup.DefaultBudgetSeconds));
        if (!await DatabaseStartup.WaitForPostgresAsync(
            database, wait, time,
            (attempt, e) => Log.Information(
                "The database isn't reachable yet (attempt {Attempt}): {Error}", attempt, DatabaseStartup.Describe(e)),
            // Nothing to cancel with: the host's SIGTERM handling only starts in RunAsync, after this.
            CancellationToken.None))
        {
            Log.Warning("The database didn't answer within {Seconds}s; migrating anyway", wait.Budget.TotalSeconds);
        }

        // EF Core takes a database lock while migrating, so pods starting together don't race.
        await db.Database.MigrateAsync();
    }

    // Forwarded headers first: everything after sees the real client address and scheme.
    app.UseForwardedHeaders();
    app.UseRequestTelemetry();
    // Tells the page whether to send its telemetry (a Server-Timing mark), before the files are served.
    app.UseClientTelemetryMark();
    app.UseAdminPageGuard();
    app.UseClientFiles();
    app.UseAuthentication();
    app.UseAuthorization();
    // After authentication: the browsers' telemetry is limited per player.
    app.UseRateLimiter();

    app.MapAppHealthChecks();
    // The old probe paths, for a chart from before /health/live and /health/ready.
    app.MapHealthChecks("/healthz", new() { Predicate = _ => false });
    app.MapHealthChecks("/readyz", new() { Predicate = check => check.Tags.Contains(HealthCheckExtensions.ReadyTag) });
    app.MapAuthEndpoints(providers);
    app.MapClientTelemetry();
    app.MapAdminEndpoints();
    app.MapHub<GameHub>("/hubs/game").RequireAuthorization(AdminEndpoints.NotBannedPolicy);
    app.MapFallbackToFile("index.html");

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    // HostAbortedException is EF Core's design-time tooling stopping the host on purpose.
    Log.Fatal(ex, "The server stopped");
    Environment.ExitCode = 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

/// <summary>The entry point, public for the integration tests' WebApplicationFactory.</summary>
public partial class Program;

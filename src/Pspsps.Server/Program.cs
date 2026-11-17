using System.Text.Json.Serialization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pspsps.Application;
using Pspsps.Infrastructure;
using Pspsps.Infrastructure.Persistence;
using Pspsps.Server;
using Pspsps.Server.Auth;
using Pspsps.Server.Game;
using Pspsps.Server.Hubs;
using Pspsps.Server.Telemetry;
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
    var signalR = builder.Services.AddSignalR(options => options.AddFilter<HubCallMetricsFilter>())
        .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
    if (!string.IsNullOrEmpty(redis))
    {
        signalR.AddStackExchangeRedis(redis, options =>
            options.Configuration.ChannelPrefix = RedisChannel.Literal("pspsps"));
    }

    builder.Services.AddSingleton<IRoomBroadcaster, HubRoomBroadcaster>();
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

    await using (var scope = app.Services.CreateAsyncScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<PspspsDbContext>();
        var time = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        // Waits for the database first (DatabaseStartup says why). The 90 s default, plus at most one 5 s attempt,
        // leaves room in the chart's 180 s startup probe (2 s x 90) for the migration and its retries.
        var wait = new DatabaseWaitOptions(
            Budget: TimeSpan.FromSeconds(app.Configuration.GetValue("Database:StartupWaitSeconds", 90)),
            Interval: TimeSpan.FromSeconds(2), AttemptTimeout: TimeSpan.FromSeconds(5));
        if (!await DatabaseStartup.WaitAsync(
            async ct =>
            {
                // A plain connection, so a failed attempt shows its real error.
                await using var connection = new NpgsqlConnection(database);
                await connection.OpenAsync(ct);
            },
            // PostgreSQL answered, even with an error (a missing database, a wrong password): the migration runs now
            // and creates the database or fails with that error.
            e => e is PostgresException,
            (attempt, e) => Log.Information(
                "The database isn't reachable yet (attempt {Attempt}): {Error}", attempt, e.Message),
            wait, time, (delay, ct) => Task.Delay(delay, time, ct),
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
    app.UseDefaultFiles();
    app.UseStaticFiles();
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapAppHealthChecks();
    // The old probe paths, for a chart from before /health/live and /health/ready.
    app.MapHealthChecks("/healthz", new() { Predicate = _ => false });
    app.MapHealthChecks("/readyz", new() { Predicate = check => check.Tags.Contains(HealthCheckExtensions.ReadyTag) });
    app.MapAuthEndpoints(providers);
    app.MapHub<GameHub>("/hubs/game");
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

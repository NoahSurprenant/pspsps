using System.Text.Json.Serialization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Pspsps.Application;
using Pspsps.Infrastructure;
using Pspsps.Infrastructure.Persistence;
using Pspsps.Server.Auth;
using Pspsps.Server.Game;
using Pspsps.Server.Hubs;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

var database = builder.Configuration.GetConnectionString("Pspsps")
    ?? throw new InvalidOperationException("Set ConnectionStrings:Pspsps to the PostgreSQL database.");

// Redis joins the pods into one game: Orleans' membership table and the SignalR backplane. Without it the server is a
// single local silo, which is all development and the tests need.
var redis = builder.Configuration.GetConnectionString("Redis");

builder.Services.AddApplication();
builder.Services.AddInfrastructure(database);
var providers = builder.AddGameAuthentication();

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
var signalR = builder.Services.AddSignalR()
    .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
if (!string.IsNullOrEmpty(redis))
{
    signalR.AddStackExchangeRedis(redis, options =>
        options.Configuration.ChannelPrefix = RedisChannel.Literal("pspsps"));
}

builder.Services.AddSingleton<IRoomBroadcaster, HubRoomBroadcaster>();

builder.UseOrleans(silo =>
{
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

builder.Services.AddHealthChecks().AddDbContextCheck<PspspsDbContext>(tags: ["ready"]);

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    // Behind the cluster's ingress: trust its X-Forwarded-* so OAuth redirects use https and the public host.
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    // EF Core takes a database lock while migrating, so pods starting together don't race.
    await scope.ServiceProvider.GetRequiredService<PspspsDbContext>().Database.MigrateAsync();
}

app.UseForwardedHeaders();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/healthz", new() { Predicate = _ => false });
app.MapHealthChecks("/readyz", new() { Predicate = check => check.Tags.Contains("ready") });
app.MapAuthEndpoints(providers);
app.MapHub<GameHub>("/hubs/game");
app.MapFallbackToFile("index.html");

await app.RunAsync();

/// <summary>The entry point, public for the integration tests' WebApplicationFactory.</summary>
public partial class Program;

using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Orleans.Runtime;
using Pspsps.Application.Moderation;
using Pspsps.Application.Players;
using Pspsps.Infrastructure.Persistence;
using Pspsps.Server.Auth;
using Pspsps.Server.Telemetry.Browser;

namespace Pspsps.Integration.Test;

/// <summary>
/// The real server: one local Orleans silo, no Redis, the test container's database. The grains' clock (their timers and
/// what they read as now) is <see cref="GrainClock"/>, which moves only when a test moves it; everything else keeps real
/// time.
/// </summary>
public sealed class GameFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Pspsps", connectionString);
        // Discord as a configured provider: /auth/login and /auth/link challenge it (a redirect, never a request to it).
        builder.UseSetting("Authentication:Discord:ClientId", "test-client");
        builder.UseSetting("Authentication:Discord:ClientSecret", "test-secret");
        builder.UseSetting("Content:ApiToken", "a-content-token-for-the-tests-0123456789");
        // The chat filter's word lists: the image ships none.
        builder.UseSetting("ChatFilter:Lists:0:Action", "Mask");
        builder.UseSetting("ChatFilter:Lists:0:Words:0", "darn");
        builder.UseSetting("ChatFilter:Lists:0:Words:1", "heck");
        builder.UseSetting("ChatFilter:Lists:1:Action", "Refuse");
        builder.UseSetting("ChatFilter:Lists:1:Words:0", "woof");
        // The browsers' telemetry on, forwarded to Collector.
        builder.UseSetting("ClientTelemetry:Enabled", "true");
        builder.UseSetting("ClientTelemetry:OtlpEndpoint", "http://collector.test:4318");
        // Network bans on: the key the server hashes addresses with. Requests have an address only with TestPeer's header.
        builder.UseSetting("Moderation:AddressKey", AddressKey);
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IStartupFilter, TestPeer>();
            services.AddSingleton<IStartupFilter, FakeExternalSignIn>();
            services.AddHttpClient<ClientTelemetryForwarder>().ConfigurePrimaryHttpMessageHandler(() => Collector);
            services.ConfigureDbContext<PspspsDbContext>(options =>
                options.AddInterceptors(BeforeSaving, BeforeSaving.Statements));
            services.AddAuthentication().AddRemoteScheme<RemoteAuthenticationOptions, FakeProviderHandler>(
                FakeProviderHandler.SchemeName, null, options =>
                {
                    options.CallbackPath = FakeProviderHandler.CallbackPath;
                    options.Events = new RemoteAuthenticationEvents();
                    AuthSetup.ConfigureExternal(options);
                });
        });
        builder.ConfigureAppConfiguration(configuration => configuration.Add(Admins));
        builder.ConfigureServices(services =>
        {
            services.AddKeyedSingleton<TimeProvider>(TimeProviderNames.Grains, GrainClock);
            services.AddKeyedSingleton<TimeProvider>(ModerationClock.Key, ModerationTime);
        });
    }

    /// <summary>The server's secret for network hashes.</summary>
    public const string AddressKey = "integration-test-key-0123456789abcdef";

    public FakeTimeProvider GrainClock { get; } = new(DateTimeOffset.UtcNow);

    /// <summary>Where the browsers' telemetry goes.</summary>
    public FakeCollector Collector { get; } = new();

    /// <summary>
    /// The clock bans and mutes are timed by: it moves only when a test moves it. On a whole second, as the database keeps
    /// times to the microsecond: a time read back equals the one decided.
    /// </summary>
    public FakeTimeProvider ModerationTime { get; } =
        new(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));

    /// <summary>The server's admins, by player ID: none until a test adds one.</summary>
    public TestAdmins Admins { get; } = new();

    /// <summary>Lets a test act in the server just before it saves a change to a player.</summary>
    public BeforeSaving BeforeSaving { get; } = new();

    public T Get<T>()
        where T : notnull => Services.CreateScope().ServiceProvider.GetRequiredService<T>();

    /// <summary>Signs in a new guest and opens their hub connection (long polling through the test server).</summary>
    public async Task<(PlayerProfile Player, HubConnection Hub)> ConnectGuestAsync(string? from = null)
    {
        var (player, cookie) = await SignInGuestAsync(from);
        return (player, await ConnectAsync(cookie, from));
    }

    /// <summary>Signs in a new guest (from the address <paramref name="from"/>, if any); returns them and their cookie.</summary>
    public async Task<(PlayerProfile Player, string Cookie)> SignInGuestAsync(string? from = null)
    {
        using var client = CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        if (from is not null)
        {
            client.DefaultRequestHeaders.Add(TestPeer.Header, from);
        }

        using var response = await client.PostAsync(new Uri("/auth/guest", UriKind.Relative), null);
        response.EnsureSuccessStatusCode();
        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("pspsps=", StringComparison.Ordinal));
        var player = (await response.Content.ReadFromJsonAsync<PlayerProfile>(JsonOptions.Web))!;
        return (player, cookie.Split(';')[0]);
    }

    /// <summary>
    /// Opens a hub connection with <paramref name="cookie"/> (from the address <paramref name="from"/>, if any): another
    /// tab of the same player, say.
    /// </summary>
    public async Task<HubConnection> ConnectAsync(string cookie, string? from = null)
    {
        var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(Server.BaseAddress, "/hubs/game"), options =>
            {
                options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => Server.CreateHandler();
                options.Headers["Cookie"] = cookie;
                if (from is not null)
                {
                    options.Headers[TestPeer.Header] = from;
                }
            })
            .AddJsonProtocol(options => options.PayloadSerializerOptions = JsonOptions.Web)
            .Build();
        await hub.StartAsync();
        return hub;
    }
}

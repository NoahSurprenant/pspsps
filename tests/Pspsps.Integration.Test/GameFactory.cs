using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Orleans.Runtime;
using Pspsps.Application.Players;
using Pspsps.Server.Auth;

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
        // The chat filter's word lists: the image ships none.
        builder.UseSetting("ChatFilter:Lists:0:Action", "Mask");
        builder.UseSetting("ChatFilter:Lists:0:Words:0", "darn");
        builder.UseSetting("ChatFilter:Lists:0:Words:1", "heck");
        builder.UseSetting("ChatFilter:Lists:1:Action", "Refuse");
        builder.UseSetting("ChatFilter:Lists:1:Words:0", "woof");
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IStartupFilter, FakeExternalSignIn>();
            services.AddAuthentication().AddRemoteScheme<RemoteAuthenticationOptions, FakeProviderHandler>(
                FakeProviderHandler.SchemeName, null, options =>
                {
                    options.CallbackPath = FakeProviderHandler.CallbackPath;
                    options.Events = new RemoteAuthenticationEvents();
                    AuthSetup.ConfigureExternal(options);
                });
        });
        builder.ConfigureServices(services =>
            services.AddKeyedSingleton<TimeProvider>(TimeProviderNames.Grains, GrainClock));
    }

    public FakeTimeProvider GrainClock { get; } = new(DateTimeOffset.UtcNow);

    public T Get<T>()
        where T : notnull => Services.CreateScope().ServiceProvider.GetRequiredService<T>();

    /// <summary>Signs in a new guest and opens their hub connection (long polling through the test server).</summary>
    public async Task<(PlayerProfile Player, HubConnection Hub)> ConnectGuestAsync()
    {
        var (player, cookie) = await SignInGuestAsync();
        return (player, await ConnectAsync(cookie));
    }

    /// <summary>Signs in a new guest; returns them and their cookie.</summary>
    public async Task<(PlayerProfile Player, string Cookie)> SignInGuestAsync()
    {
        using var client = CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var response = await client.PostAsync(new Uri("/auth/guest", UriKind.Relative), null);
        response.EnsureSuccessStatusCode();
        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("pspsps=", StringComparison.Ordinal));
        var player = (await response.Content.ReadFromJsonAsync<PlayerProfile>(JsonOptions.Web))!;
        return (player, cookie.Split(';')[0]);
    }

    /// <summary>Opens a hub connection with <paramref name="cookie"/>: another tab of the same player, say.</summary>
    public async Task<HubConnection> ConnectAsync(string cookie)
    {
        var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(Server.BaseAddress, "/hubs/game"), options =>
            {
                options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => Server.CreateHandler();
                options.Headers["Cookie"] = cookie;
            })
            .AddJsonProtocol(options => options.PayloadSerializerOptions = JsonOptions.Web)
            .Build();
        await hub.StartAsync();
        return hub;
    }
}

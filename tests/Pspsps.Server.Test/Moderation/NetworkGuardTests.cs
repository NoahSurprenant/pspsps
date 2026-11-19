using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Pspsps.Application.Moderation;
using Pspsps.Application.Players;
using Pspsps.Domain.Moderation;
using Pspsps.Domain.Players;
using Pspsps.Server.Auth;
using Pspsps.Server.Game;
using Pspsps.Server.Hubs;
using Pspsps.Server.Moderation;

namespace Pspsps.Server.Test.Moderation;

/// <summary>
/// Network bans where they are held: guest sign-up, sign-in, /api/me, the hub's door (negotiate) and its calls; never for
/// an admin. Addresses from the documentation ranges.
/// </summary>
public sealed class NetworkGuardTests
{
    private const string Banned = "198.51.100.1";
    private const string Elsewhere = "198.51.100.2";
    private static readonly ClientAddresses Addresses = ClientAddresses.WithKey("a-test-key-of-at-least-32-characters");
    private static readonly PlayerProfile Felix = new(Guid.NewGuid(), "Felix", FurColor.Black, 0, IsGuest: true);
    private static readonly PlayerProfile Owner = new(Guid.NewGuid(), "Owner", FurColor.White, 0, IsGuest: false);
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero));
    private IAddressBanService _networks = null!;
    private IModerationService _moderation = null!;
    private IPlayerService _players = null!;
    private IAdmins _admins = null!;
    private ServiceProvider _services = null!;
    private AddressGuard _guard = null!;
    private Sanction _ban = null!;

    [SetUp]
    public void Create()
    {
        _ban = new Sanction("Ban evasion", _time.GetUtcNow(), _time.GetUtcNow().AddHours(24), Guid.NewGuid());
        _networks = Substitute.For<IAddressBanService>();
        _networks.BanOfAsync(Hash(Banned), Arg.Any<CancellationToken>()).Returns(_ban);
        _moderation = Substitute.For<IModerationService>();
        _moderation.StandingAsync(default, default).ReturnsForAnyArgs(Standing.None);
        _players = Substitute.For<IPlayerService>();
        _players.CreateGuestAsync(Arg.Any<CancellationToken>()).Returns(Felix);
        _players.FindAsync(Felix.Id, Arg.Any<CancellationToken>()).Returns(Felix);
        _players.FindAsync(Owner.Id, Arg.Any<CancellationToken>()).Returns(Owner);
        _admins = Substitute.For<IAdmins>();
        _admins.Current.Returns(new HashSet<Guid> { Owner.Id });
        _moderation.IsAdmin(Owner.Id).Returns(true);
        _services = new ServiceCollection()
            .AddScoped(_ => _networks)
            .AddScoped(_ => _moderation)
            .BuildServiceProvider();
        _guard = Guard(Addresses);
    }

    [TearDown]
    public void Dispose() => _services.Dispose();

    private static string Hash(string address) => Addresses.HashOf(IPAddress.Parse(address));

    private IServiceScopeFactory Scopes => _services.GetRequiredService<IServiceScopeFactory>();

    private AddressGuard Guard(ClientAddresses addresses) => new(
        addresses, new AddressBanCache(Scopes, _time), _admins, Scopes, _time, NullLogger<AddressGuard>.Instance);

    private static DefaultHttpContext From(string address, Guid? playerId = null)
    {
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse(address);
        if (playerId is { } id)
        {
            http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(PlayerClaims.PlayerIdType, id.ToString())], "test"));
        }

        return http;
    }

    [Test]
    public async Task ABannedNetwork_IsHeldToForAnyone_ButNeverForAnAdmin()
    {
        Assert.That(await _guard.BanOfAsync(From(Banned), null, CancellationToken.None), Is.EqualTo(_ban), "a new guest");
        Assert.That(await _guard.BanOfAsync(From(Banned), Felix.Id, CancellationToken.None), Is.EqualTo(_ban));
        Assert.That(await _guard.BanOfAsync(From(Banned), Owner.Id, CancellationToken.None), Is.Null, "an admin");
        Assert.That(await _guard.BanOfAsync(From(Elsewhere), Felix.Id, CancellationToken.None), Is.Null);
        Assert.That(await Guard(ClientAddresses.Off).BanOfAsync(From(Banned), Felix.Id, CancellationToken.None), Is.Null,
            "network bans off");
    }

    [Test]
    public async Task ANetworksBan_IsReadOnceEvery5Seconds_AndEndsAtItsTime()
    {
        var cache = new AddressBanCache(Scopes, _time);

        Assert.That(await cache.BanOfAsync(Hash(Banned), CancellationToken.None), Is.EqualTo(_ban));
        _networks.BanOfAsync(Hash(Banned), Arg.Any<CancellationToken>()).Returns((Sanction?)null);
        Assert.That(await cache.BanOfAsync(Hash(Banned), CancellationToken.None), Is.EqualTo(_ban), "read 5 s ago: kept");
        await _networks.Received(1).BanOfAsync(Hash(Banned), Arg.Any<CancellationToken>());

        _time.Advance(AddressBanCache.KeepFor);
        Assert.That(await cache.BanOfAsync(Hash(Banned), CancellationToken.None), Is.Null, "lifted on another pod: seen now");

        cache.Know(Hash(Banned), _ban);
        Assert.That(await cache.BanOfAsync(Hash(Banned), CancellationToken.None), Is.EqualTo(_ban), "banned here: at once");
        _time.Advance(TimeSpan.FromHours(24));
        _networks.BanOfAsync(Hash(Banned), Arg.Any<CancellationToken>()).Returns(_ban);
        Assert.That(await cache.BanOfAsync(Hash(Banned), CancellationToken.None), Is.Null, "over at its time");
    }

    [Test]
    public async Task TheHubsDoor_IsShutToABannedNetwork_ButNotToAnAdmin()
    {
        var handler = new NotBannedHandler(new StandingCache(Scopes, _time), _guard);

        async Task<bool> Lets(string address, Guid playerId)
        {
            var http = From(address, playerId);
            var context = new AuthorizationHandlerContext([new NotBannedRequirement()], http.User, http);
            await handler.HandleAsync(context);
            return context.HasSucceeded;
        }

        Assert.That(await Lets(Banned, Felix.Id), Is.False);
        Assert.That(await Lets(Elsewhere, Felix.Id), Is.True);
        Assert.That(await Lets(Banned, Owner.Id), Is.True, "an admin");
    }

    [Test]
    public async Task AConnectionOnABannedNetwork_IsRefusedAsItOpens()
    {
        var filter = new ModerationHubFilter(new StandingCache(Scopes, _time), new PlayerConnections(), _guard);
        var opened = false;

        var refused = Assert.ThrowsAsync<HubException>(() => filter.OnConnectedAsync(Lifetime(Caller(Felix, Banned)), _ =>
        {
            opened = true;
            return Task.CompletedTask;
        }));

        Assert.That(refused!.Message, Is.EqualTo(_ban.SaysBanned()));
        Assert.That(opened, Is.False);
        await _networks.DidNotReceiveWithAnyArgs().SeenAsync(default, default!, default);
    }

    [Test]
    public async Task AConnection_RemembersItsNetwork_JoinsItsGroup_AndItsCallsAreRefusedOnceTheNetworkIsBanned()
    {
        var connections = new PlayerConnections();
        var filter = new ModerationHubFilter(new StandingCache(Scopes, _time), connections, _guard);
        var caller = Caller(Felix, Elsewhere);
        var lifetime = Lifetime(caller);

        await filter.OnConnectedAsync(lifetime, _ => Task.CompletedTask);

        await _networks.Received(1).SeenAsync(Felix.Id, Hash(Elsewhere), Arg.Any<CancellationToken>());
        await lifetime.Hub.Groups.Received(1).AddToGroupAsync(caller.ConnectionId, AddressGuard.GroupOf(Hash(Elsewhere)));
        Assert.That(connections.OnNetwork(Hash(Elsewhere)), Is.EqualTo(new[] { (Felix.Id, caller) }));

        _networks.BanOfAsync(Hash(Elsewhere), Arg.Any<CancellationToken>()).Returns(_ban);
        _time.Advance(AddressBanCache.KeepFor);
        var refused = Assert.ThrowsAsync<HubException>(async () => await filter.InvokeMethodAsync(
            new HubInvocationContext(caller, Substitute.For<IServiceProvider>(), lifetime.Hub,
                typeof(GameHub).GetMethod(nameof(GameHub.Say))!, ["hello"]),
            _ => ValueTask.FromResult<object?>(null)));
        Assert.That(refused!.Message, Is.EqualTo(_ban.SaysBanned()));
        caller.Received(1).Abort();
    }

    [Test]
    public async Task AnAdminsConnection_OnABannedNetwork_OpensAndIsNeitherHeldNorGrouped()
    {
        var connections = new PlayerConnections();
        var filter = new ModerationHubFilter(new StandingCache(Scopes, _time), connections, _guard);
        var caller = Caller(Owner, Banned);
        var lifetime = Lifetime(caller);

        await filter.OnConnectedAsync(lifetime, _ => Task.CompletedTask);

        await lifetime.Hub.Groups.DidNotReceiveWithAnyArgs().AddToGroupAsync(default!, default!);
        Assert.That(connections.OnNetwork(Hash(Banned)), Is.Empty);
        Assert.That(connections.Of(Owner.Id), Is.EqualTo(new[] { caller }));
    }

    [Test]
    public async Task NetworksNotSeenFor7Days_AreForgottenEveryHour()
    {
        var pruning = new AddressPruning(Scopes, Addresses, _time, NullLogger<AddressPruning>.Instance);
        using var stop = new CancellationTokenSource();

        await pruning.StartAsync(stop.Token);
        await Eventually(() => _networks.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IAddressBanService.PruneAsync)) == 1);
        _time.Advance(AddressPruning.Every);
        await Eventually(() => _networks.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IAddressBanService.PruneAsync)) == 2);
        await pruning.StopAsync(CancellationToken.None);
        await _networks.Received(2).PruneAsync(forgetAll: false, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task WithNetworkBansOff_PruningForgetsEveryNetworkAndBan()
    {
        var pruning = new AddressPruning(Scopes, ClientAddresses.Off, _time, NullLogger<AddressPruning>.Instance);

        await pruning.PruneAsync(CancellationToken.None);

        await _networks.Received(1).PruneAsync(forgetAll: true, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ANetworkSeenWithinTheHour_IsntSavedAgain()
    {
        var filter = new ModerationHubFilter(new StandingCache(Scopes, _time), new PlayerConnections(), _guard);

        await filter.OnConnectedAsync(Lifetime(Caller(Felix, Elsewhere)), _ => Task.CompletedTask);
        _time.Advance(TimeSpan.FromMinutes(59));
        await filter.OnConnectedAsync(Lifetime(Caller(Felix, Elsewhere)), _ => Task.CompletedTask);
        await _networks.Received(1).SeenAsync(Felix.Id, Hash(Elsewhere), Arg.Any<CancellationToken>());

        _time.Advance(TimeSpan.FromMinutes(1));
        await filter.OnConnectedAsync(Lifetime(Caller(Felix, Elsewhere)), _ => Task.CompletedTask);
        await _networks.Received(2).SeenAsync(Felix.Id, Hash(Elsewhere), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AGuest_FromABannedNetwork_IsRefusedWithWhy_AndNoCatIsMade()
    {
        await using var app = await StartAsync();

        var refused = await SendAsync(app, "POST", "/auth/guest", Banned);
        var made = await SendAsync(app, "POST", "/auth/guest", Elsewhere);

        Assert.That(refused.Response.StatusCode, Is.EqualTo(StatusCodes.Status403Forbidden));
        var body = await JsonSerializer.DeserializeAsync<BannedView>(refused.Response.Body, JsonOptions);
        Assert.That(body, Is.EqualTo(new BannedView(_ban.SaysBanned(), new SanctionView("Ban evasion", _ban.Until))));
        Assert.That(made.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
        await _players.Received(1).CreateGuestAsync(Arg.Any<CancellationToken>());
        await _networks.Received(1).SeenAsync(Felix.Id, Hash(Elsewhere), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ASignIn_FromABannedNetwork_IsRefused_ButAnAdminsIsNot()
    {
        var key = "login-" + Guid.NewGuid();
        await using var app = await StartAsync();

        Assert.That(await SignInAsync(app, key, Banned), Is.EqualTo(AuthRedirects.SignInBanned));
        await _players.DidNotReceiveWithAnyArgs().SignInWithLoginAsync(default, default!, default, default);

        _players.FindByLoginAsync(LoginProvider.Discord, key, Arg.Any<CancellationToken>()).Returns(Owner.Id);
        _players.SignInWithLoginAsync(LoginProvider.Discord, key, Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Owner);
        Assert.That(await SignInAsync(app, key, Banned), Is.EqualTo(AuthRedirects.Home), "the owner can't lock themself out");
    }

    [Test]
    public async Task AnAdminsSignIn_DoesntKeepTheirNetwork()
    {
        var key = "login-" + Guid.NewGuid();
        _players.FindByLoginAsync(LoginProvider.Discord, key, Arg.Any<CancellationToken>()).Returns(Owner.Id);
        _players.SignInWithLoginAsync(LoginProvider.Discord, key, Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Owner);
        await using var app = await StartAsync();

        Assert.That(await SignInAsync(app, key, Elsewhere), Is.EqualTo(AuthRedirects.Home));

        await _networks.DidNotReceiveWithAnyArgs().SeenAsync(default, default!, default);
    }

    [Test]
    public async Task WithNetworkBansOff_ASignInLooksNothingUp()
    {
        var key = "login-" + Guid.NewGuid();
        _players.SignInWithLoginAsync(LoginProvider.Discord, key, Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Felix);
        await using var app = await StartAsync(Guard(ClientAddresses.Off));

        Assert.That(await SignInAsync(app, key, Banned), Is.EqualTo(AuthRedirects.Home));

        await _players.DidNotReceiveWithAnyArgs().FindByLoginAsync(default, default!, default);
        await _networks.DidNotReceiveWithAnyArgs().BanOfAsync(default!, default);
    }

    [Test]
    public async Task Me_OnABannedNetwork_SaysBanned_ButNotToAnAdmin()
    {
        await using var app = await StartAsync();

        var felix = await SendAsync(app, "GET", "/api/me", Banned, Felix.Id);
        var owner = await SendAsync(app, "GET", "/api/me", Banned, Owner.Id);
        var elsewhere = await SendAsync(app, "GET", "/api/me", Elsewhere, Felix.Id);

        Assert.That((await JsonSerializer.DeserializeAsync<MeView>(felix.Response.Body, JsonOptions))!.Ban,
            Is.EqualTo(new SanctionView("Ban evasion", _ban.Until)));
        Assert.That((await JsonSerializer.DeserializeAsync<MeView>(owner.Response.Body, JsonOptions))!.Ban, Is.Null);
        Assert.That((await JsonSerializer.DeserializeAsync<MeView>(elsewhere.Response.Body, JsonOptions))!.Ban, Is.Null);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    // The sign-in endpoints as Program.cs maps them, on a test server, with the use cases faked. A request's player is
    // the test's header (as the game's cookie would make it); a provider's return signs the external cookie in, as
    // Discord's handler would.
    private async Task<WebApplication> StartAsync(AddressGuard? guard = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.AddGameAuthentication();
        builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        builder.Services.AddSingleton(_players);
        builder.Services.AddSingleton(_moderation);
        builder.Services.AddSingleton(guard ?? _guard);
        var app = builder.Build();
        app.Use(async (http, next) =>
        {
            if (http.Request.Path == "/test/external")
            {
                var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, http.Request.Query["key"].ToString())], "fake");
                await http.SignInAsync(AuthSetup.ExternalScheme, new ClaimsPrincipal(identity),
                    new ExternalSignInIntent("Discord").ToProperties());
                http.Response.Redirect(ExternalSignInIntent.CallbackPath);
                return;
            }

            await next(http);
        });
        app.UseAuthentication();
        app.Use((http, next) =>
        {
            if (Guid.TryParse(http.Request.Headers["X-Test-Player"].ToString(), out var playerId))
            {
                http.User = new ClaimsPrincipal(
                    new ClaimsIdentity([new Claim(PlayerClaims.PlayerIdType, playerId.ToString())], AuthSetup.GameScheme));
            }

            return next(http);
        });
        app.MapAuthEndpoints([]);
        await app.StartAsync();
        return app;
    }

    private static Task<HttpContext> SendAsync(
        WebApplication app, string method, string path, string from, Guid? playerId = null, string? cookie = null) =>
        app.GetTestServer().SendAsync(c =>
        {
            c.Request.Method = method;
            var query = path.IndexOf('?', StringComparison.Ordinal);
            c.Request.Path = query >= 0 ? path[..query] : path;
            if (query >= 0)
            {
                c.Request.QueryString = new QueryString(path[query..]);
            }

            c.Connection.RemoteIpAddress = IPAddress.Parse(from);
            if (playerId is { } id)
            {
                c.Request.Headers["X-Test-Player"] = id.ToString();
            }

            if (cookie is not null)
            {
                c.Request.Headers.Cookie = cookie;
            }
        });

    // Back from the (fake) provider, then /auth/callback: where it sends the browser.
    private static async Task<string> SignInAsync(WebApplication app, string key, string from)
    {
        var back = await SendAsync(app, "GET", "/test/external?key=" + Uri.EscapeDataString(key), from);
        var cookie = back.Response.Headers.SetCookie.Select(c => c!.Split(';')[0]).Single(c => c.StartsWith("pspsps-external=", StringComparison.Ordinal));
        var callback = await SendAsync(app, "GET", ExternalSignInIntent.CallbackPath, from, cookie: cookie);
        return callback.Response.Headers.Location.ToString();
    }

    private static HubCallerContext Caller(PlayerProfile player, string from)
    {
        var features = new FeatureCollection();
        features.Set<IHttpContextFeature>(new HttpContextFeature { HttpContext = From(from) });
        var context = Substitute.For<HubCallerContext>();
        context.ConnectionId.Returns(player.DisplayName + "-tab");
        context.User.Returns(PlayerClaims.For(player));
        context.Features.Returns(features);
        context.Items.Returns(new Dictionary<object, object?>());
        context.ConnectionAborted.Returns(CancellationToken.None);
        return context;
    }

    private static HubLifetimeContext Lifetime(HubCallerContext connection)
    {
        var hub = Substitute.For<Hub>();
        hub.Groups = Substitute.For<IGroupManager>();
        return new(connection, Substitute.For<IServiceProvider>(), hub);
    }

    private static async Task Eventually(Func<bool> condition)
    {
        for (var tries = 0; tries < 200 && !condition(); tries++)
        {
            await Task.Delay(10);
        }

        Assert.That(condition(), Is.True);
    }

    private sealed class HttpContextFeature : IHttpContextFeature
    {
        public HttpContext? HttpContext { get; set; }
    }
}

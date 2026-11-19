using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Pspsps.Application.Moderation;
using Pspsps.Application.Players;
using Pspsps.Domain.Moderation;
using Pspsps.Domain.Players;
using Pspsps.Infrastructure.Persistence;
using Pspsps.Server.Game;
using Pspsps.Server.Moderation;

namespace Pspsps.Integration.Test;

/// <summary>
/// Network bans through the hosted server, each request from an address of a test's own (<see cref="TestPeer"/>; the
/// documentation ranges): the end-to-end tests' server sees one address for every browser, so a network ban there would
/// lock the whole run out.
/// </summary>
public sealed class NetworkBanTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
    private static readonly ClientAddresses Hashes = ClientAddresses.WithKey(GameFactory.AddressKey);
    private static GameFactory Game => PostgresFixture.Game;

    [Test]
    public async Task ABannedGuestsNetwork_GetsNoNewGuest_ThePageIsToldWhy_AndAnotherNetworkDoes()
    {
        const string Home = "192.0.2.10";
        var (_, adminHub) = await AdminAsync();
        var (felix, felixHub) = await Game.ConnectGuestAsync(from: Home);
        await using var _ = adminHub;
        await using var __ = felixHub;

        var networks = await BanWithNetworksAsync(adminHub, felix.Id, "Came back to spam", "24h");

        var network = networks.Single();
        Assert.Multiple(() =>
        {
            Assert.That(network.Key, Is.EqualTo(Hashes.HashOf(IPAddress.Parse(Home))));
            Assert.That(network.Label, Is.EqualTo(network.Key[..8]));
            Assert.That(network.Players, Is.EqualTo(1));
            Assert.That(network.Ban?.Reason, Is.EqualTo("Came back to spam"), "the cat's ban's reason");
            Assert.That(network.Ban?.Until, Is.EqualTo(Game.ModerationTime.GetUtcNow().AddHours(24)));
        });

        using var again = await GuestFromAsync(Home);
        Assert.That(again.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        var refused = await again.Content.ReadFromJsonAsync<BannedView>(JsonOptions.Web);
        Assert.That(refused!.Message, Does.StartWith("Your cat is banned until ").And.EndWith("Why: Came back to spam"));
        Assert.That(refused.Ban, Is.EqualTo(network.Ban));

        using var elsewhere = await GuestFromAsync("198.51.100.10");
        Assert.That(elsewhere.StatusCode, Is.EqualTo(HttpStatusCode.OK), "another network gets a guest");
    }

    [Test]
    public async Task AnotherCatOnABannedNetwork_IsToldAtOnce_Closed_AndRefused_UntilTheBanIsLifted()
    {
        const string School = "192.0.2.20";
        var (_, adminHub) = await AdminAsync();
        var (felix, felixHub) = await Game.ConnectGuestAsync(from: School);
        var (tom, tomCookie) = await Game.SignInGuestAsync(from: School);
        var tomHub = await Game.ConnectAsync(tomCookie, from: School);
        await using var _ = adminHub;
        await using var __ = felixHub;
        await using var ___ = tomHub;
        var tomSees = new Recorder(tomHub);
        var closed = new TaskCompletionSource();
        tomHub.Closed += error =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };
        await tomHub.InvokeAsync<RoomSnapshot>("EnterWorld", "siamese");

        var networks = await BanWithNetworksAsync(adminHub, felix.Id, "Spam", "24h");
        Assert.That(networks.Single().Players, Is.EqualTo(2), "a shared network shows how many cats were on it");

        // Tom isn't banned, but his network is: told why, as a banned cat is, and closed.
        Assert.That((await tomSees.Next<SanctionView>("Banned", any => true)).Reason, Is.EqualTo("Spam"));
        Assert.That(await Task.WhenAny(closed.Task, Task.Delay(Wait)), Is.SameAs(closed.Task), "the server closes it");
        var refused = Assert.ThrowsAsync<HttpRequestException>(() => Game.ConnectAsync(tomCookie, from: School));
        Assert.That(refused!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That((await MeAsync(tomCookie, from: School)).Ban?.Reason, Is.EqualTo("Spam"), "the page says why");
        Assert.That((await MeAsync(tomCookie, from: "198.51.100.20")).Ban, Is.Null, "his cat isn't banned elsewhere");
        await using (var fromElsewhere = await Game.ConnectAsync(tomCookie, from: "198.51.100.20"))
        {
            Assert.That((await fromElsewhere.InvokeAsync<RoomSnapshot>("EnterWorld", "siamese")).RoomId, Is.EqualTo("town-square"));
        }

        var card = await adminHub.InvokeAsync<PlayerCardView>("LiftNetworkBans", felix.Id);

        Assert.That(card.Moderation?.Networks?.Single().Ban, Is.Null);
        Assert.That(card.Moderation?.Ban?.Reason, Is.EqualTo("Spam"), "the cat stays banned: lifted apart");
        await using var back = await Game.ConnectAsync(tomCookie, from: School);
        Assert.That((await back.InvokeAsync<RoomSnapshot>("EnterWorld", "siamese")).RoomId, Is.EqualTo("town-square"));
        using var guest = await GuestFromAsync(School);
        Assert.That(guest.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task AnAdminOnABannedNetwork_IsLetIn()
    {
        const string Cafe = "192.0.2.30";
        var (admin, adminHub) = await AdminAsync();
        var (felix, felixHub) = await Game.ConnectGuestAsync(from: Cafe);
        await using var _ = adminHub;
        await using var __ = felixHub;
        await BanWithNetworksAsync(adminHub, felix.Id, "Spam", "7d");
        // The owner, signed in on that network: listed as an admin.
        var key = PlayerServiceTests.NewKey();
        var owner = await Game.Get<IPlayerService>().SignInWithLoginAsync(LoginProvider.Discord, key, "Owner", CancellationToken.None);
        Game.Admins.Add(owner.Id);

        using var browser = BrowserFrom(Cafe);
        var landed = await FinishExternalSignInAsync(browser, FakeExternalSignIn.Url("Discord", key));
        Assert.That(landed, Is.EqualTo("/"), "an admin signs in");
        var me = await browser.GetFromJsonAsync<MeView>(new Uri("/api/me", UriKind.Relative), JsonOptions.Web);
        Assert.That((me!.Id, me.Ban), Is.EqualTo((owner.Id, (SanctionView?)null)));

        // And an admin's tab on it plays: its connection is let in, and its calls.
        await using var hub = await Game.ConnectAsync(AdminCookies[admin.Id], from: Cafe);
        Assert.That((await hub.InvokeAsync<RoomSnapshot>("EnterWorld", "siamese")).RoomId, Is.EqualTo("town-square"));
        Assert.That((await MeAsync(AdminCookies[admin.Id], from: Cafe)).Ban, Is.Null);
        using var guest = await GuestFromAsync(Cafe);
        Assert.That(guest.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), "a new guest there is nobody's admin");
    }

    [Test]
    public async Task ASignInFromABannedNetwork_IsRefused_WithoutMakingACat_AndThePageAsksWhy()
    {
        const string Library = "192.0.2.40";
        var (_, adminHub) = await AdminAsync();
        var (felix, felixHub) = await Game.ConnectGuestAsync(from: Library);
        await using var _ = adminHub;
        await using var __ = felixHub;
        await BanWithNetworksAsync(adminHub, felix.Id, "Ban evasion", "24h");
        var key = PlayerServiceTests.NewKey();

        using var browser = BrowserFrom(Library);
        var landed = await FinishExternalSignInAsync(browser, FakeExternalSignIn.Url("Google", key));

        Assert.That(landed, Is.EqualTo("/?signin=banned"));
        Assert.That(await Game.Get<IPlayerService>().FindByLoginAsync(LoginProvider.Google, key, CancellationToken.None),
            Is.Null, "no cat made");
        using var me = await browser.GetAsync(new Uri("/api/me", UriKind.Relative));
        Assert.That(me.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), "not signed in");
        var why = await browser.GetFromJsonAsync<SanctionView>(new Uri("/auth/ban", UriKind.Relative), JsonOptions.Web);
        Assert.That(why!.Reason, Is.EqualTo("Ban evasion"));

        using var elsewhere = BrowserFrom("198.51.100.40");
        Assert.That(await FinishExternalSignInAsync(elsewhere, FakeExternalSignIn.Url("Google", key)), Is.EqualTo("/"));
        using var fine = await elsewhere.GetAsync(new Uri("/auth/ban", UriKind.Relative));
        Assert.That(fine.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task AnIPv6Network_IsBannedByIts64()
    {
        var (_, adminHub) = await AdminAsync();
        var (felix, felixHub) = await Game.ConnectGuestAsync(from: "2001:db8:47:1::5");
        await using var _ = adminHub;
        await using var __ = felixHub;

        await BanWithNetworksAsync(adminHub, felix.Id, "Spam", "24h");

        using var sameHousehold = await GuestFromAsync("2001:db8:47:1:abcd::99");
        using var nextDoor = await GuestFromAsync("2001:db8:47:2::5");
        Assert.That(sameHousehold.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), "a new address in the same /64");
        Assert.That(nextDoor.StatusCode, Is.EqualTo(HttpStatusCode.OK), "another /64");
    }

    [Test]
    public async Task TheAddress_IsNeverStoredInPlainText()
    {
        const string Address = "203.0.113.47";
        var (_, adminHub) = await AdminAsync();
        var (felix, felixHub) = await Game.ConnectGuestAsync(from: Address);
        await using var _ = adminHub;
        await using var __ = felixHub;
        await BanWithNetworksAsync(adminHub, felix.Id, "Spam", "24h");
        await adminHub.InvokeAsync<PlayerCardView>("LiftNetworkBans", felix.Id);

        var db = Game.Get<PspspsDbContext>();
        var rows = new List<string>();
        // Every row of the tables network bans touch, whole, as text.
        FormattableString[] tables =
        [
            $"SELECT t::text AS \"Value\" FROM player_addresses AS t",
            $"SELECT t::text AS \"Value\" FROM address_bans AS t",
            $"SELECT t::text AS \"Value\" FROM moderation_entries AS t",
            $"SELECT t::text AS \"Value\" FROM player_moderation AS t",
            $"SELECT t::text AS \"Value\" FROM players AS t",
        ];
        foreach (var table in tables)
        {
            rows.AddRange(await db.Database.SqlQuery<string>(table).ToListAsync());
        }

        var bytes = Convert.ToHexStringLower(IPAddress.Parse(Address).GetAddressBytes());
        Assert.That(rows, Has.None.Contains(Address).And.None.Contains(bytes));
        var kept = db.PlayerAddresses.AsNoTracking().Where(a => a.PlayerId == felix.Id).Select(a => a.AddressHash).ToList();
        Assert.That(kept, Is.EqualTo(new[] { Hashes.HashOf(IPAddress.Parse(Address)) }), "a keyed hash of it, only");
        var trail = db.ModerationEntries.AsNoTracking()
            .Where(e => e.PlayerId == felix.Id && e.Address != null)
            .Select(e => new { e.Kind, e.Address })
            .ToList();
        Assert.That(trail.Select(e => e.Kind), Is.EquivalentTo(new[] { ModerationKind.AddressBanned, ModerationKind.AddressBanLifted }));
        Assert.That(trail.Select(e => e.Address), Has.All.EqualTo(kept[0][..8]), "the audit trail has the label");
    }

    [Test]
    public async Task ANetwork_IsForgotten7DaysAfterItWasLastSeen_AndAnEndedBanWithIt()
    {
        var (_, adminHub) = await AdminAsync();
        var (felix, felixHub) = await Game.ConnectGuestAsync(from: "203.0.113.70");
        await using var _ = adminHub;
        await using var __ = felixHub;
        await BanWithNetworksAsync(adminHub, felix.Id, "Spam", "24h");
        var networks = Game.Get<IAddressBanService>();

        Game.ModerationTime.Advance(TimeSpan.FromDays(7) - TimeSpan.FromSeconds(1));
        await networks.PruneAsync(CancellationToken.None);
        var db = Game.Get<PspspsDbContext>();
        Assert.That(db.PlayerAddresses.Count(a => a.PlayerId == felix.Id), Is.EqualTo(1), "kept for 7 days");
        Assert.That(db.AddressBans.Count(b => b.PlayerId == felix.Id), Is.Zero, "the day's ban is over: forgotten");

        Game.ModerationTime.Advance(TimeSpan.FromSeconds(1));
        await networks.PruneAsync(CancellationToken.None);
        Assert.That(Game.Get<PspspsDbContext>().PlayerAddresses.Count(a => a.PlayerId == felix.Id), Is.Zero, "then forgotten");
    }

    [Test]
    public async Task TheAdminPage_ListsBannedNetworks_AndLiftsThem()
    {
        var (admin, adminHub) = await AdminAsync();
        var (felix, felixHub) = await Game.ConnectGuestAsync(from: "203.0.113.80");
        await using var _ = adminHub;
        await using var __ = felixHub;
        var network = (await BanWithNetworksAsync(adminHub, felix.Id, "Spam", "7d")).Single();
        using var asAdmin = ClientFor(AdminCookies[admin.Id]);

        var listed = (await OverviewAsync(asAdmin)).NetworkBans.Single(b => b.Key == network.Key);
        Assert.Multiple(() =>
        {
            Assert.That(listed.Label, Is.EqualTo(network.Label));
            Assert.That(listed.PlayerId, Is.EqualTo(felix.Id));
            Assert.That(listed.Players, Is.EqualTo(1));
            Assert.That(listed.Reason, Is.EqualTo("Spam"));
            Assert.That(listed.Until, Is.EqualTo(Game.ModerationTime.GetUtcNow().AddDays(7)));
        });
        var line = (await OverviewAsync(asAdmin)).Recent.Single(l => l.PlayerId == felix.Id && l.Kind == "AddressBanned");
        Assert.That((line.Address, line.Reason), Is.EqualTo((network.Label, "Spam")));

        using var lift = new HttpRequestMessage(HttpMethod.Post, new Uri($"/api/admin/networks/{network.Key}/lift", UriKind.Relative));
        lift.Headers.Add("Sec-Fetch-Site", "same-origin");
        using var lifted = await asAdmin.SendAsync(lift);

        Assert.That(lifted.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That((await OverviewAsync(asAdmin)).NetworkBans.Select(b => b.Key), Does.Not.Contain(network.Key));
        using var guest = await GuestFromAsync("203.0.113.80");
        Assert.That(guest.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task ANetworkIsBanned_OnlyWithItsCat_AndNeverForGood()
    {
        var (_, adminHub) = await AdminAsync();
        var (felix, felixHub) = await Game.ConnectGuestAsync(from: "203.0.113.90");
        await using var _ = adminHub;
        await using var __ = felixHub;
        var key = (await adminHub.InvokeAsync<PlayerCardView>("GetPlayerCard", felix.Id)).Moderation!.Networks!.Single().Key;

        var notBanned = Assert.ThrowsAsync<HubException>(() =>
            adminHub.InvokeAsync<PlayerCardView>("BanNetworks", felix.Id, new[] { key }, "24h"));
        Assert.That(notBanned!.Message, Does.Contain("Ban the cat first"));
        await adminHub.InvokeAsync<PlayerCardView>("Moderate", felix.Id, "ban", "Spam", "permanent");
        var forGood = Assert.ThrowsAsync<HubException>(() =>
            adminHub.InvokeAsync<PlayerCardView>("BanNetworks", felix.Id, new[] { key }, "permanent"));
        Assert.That(forGood!.Message, Does.Contain("a day or a week"));
        var notTheirs = Assert.ThrowsAsync<HubException>(() => adminHub.InvokeAsync<PlayerCardView>(
            "BanNetworks", felix.Id, new[] { Hashes.HashOf(IPAddress.Parse("203.0.113.91")) }, "24h"));
        Assert.That(notTheirs!.Message, Does.Contain("wasn't on that network"));
        var (tom, tomHub) = await Game.ConnectGuestAsync();
        await using var ___ = tomHub;
        var notAdmin = Assert.ThrowsAsync<HubException>(() =>
            tomHub.InvokeAsync<PlayerCardView>("BanNetworks", felix.Id, new[] { key }, "24h"));
        Assert.That(notAdmin!.Message, Does.Contain(Moderator.OnlyAdmins));
        Assert.That((await tomHub.InvokeAsync<PlayerCardView>("GetPlayerCard", felix.Id)).Moderation, Is.Null,
            "and nobody else sees a cat's networks");
        Assert.That(tom.Id, Is.Not.EqualTo(felix.Id));
    }

    // Bans the cat, then every network its card shows, from the admin's hub; the networks as the card has them after.
    private static async Task<IReadOnlyList<NetworkView>> BanWithNetworksAsync(
        HubConnection adminHub, Guid playerId, string reason, string length)
    {
        var card = await adminHub.InvokeAsync<PlayerCardView>("Moderate", playerId, "ban", reason, "permanent");
        var keys = card.Moderation!.Networks!.Select(n => n.Key).ToArray();
        card = await adminHub.InvokeAsync<PlayerCardView>("BanNetworks", playerId, keys, length);
        return card.Moderation!.Networks!;
    }

    private static async Task<HttpResponseMessage> GuestFromAsync(string address)
    {
        using var client = Game.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add(TestPeer.Header, address);
        return await client.PostAsync(new Uri("/auth/guest", UriKind.Relative), null);
    }

    // A browser (cookies kept, redirects not followed) on the address <address>.
    private static HttpClient BrowserFrom(string address)
    {
        var browser = Game.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        browser.DefaultRequestHeaders.Add(TestPeer.Header, address);
        return browser;
    }

    private static async Task<string?> FinishExternalSignInAsync(HttpClient browser, Uri provider)
    {
        using var back = await browser.GetAsync(provider);
        Assert.That(back.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        using var callback = await browser.GetAsync(back.Headers.Location);
        Assert.That(callback.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        return callback.Headers.Location?.OriginalString;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, string> AdminCookies = new();

    private static async Task<(PlayerProfile Player, HubConnection Hub)> AdminAsync()
    {
        var (player, cookie) = await Game.SignInGuestAsync();
        Game.Admins.Add(player.Id);
        AdminCookies[player.Id] = cookie;
        return (player, await Game.ConnectAsync(cookie));
    }

    private static HttpClient ClientFor(string cookie, string? from = null)
    {
        var client = Game.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        if (from is not null)
        {
            client.DefaultRequestHeaders.Add(TestPeer.Header, from);
        }

        return client;
    }

    private static async Task<AdminOverviewView> OverviewAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<AdminOverviewView>(new Uri("/api/admin/overview", UriKind.Relative), JsonOptions.Web))!;

    private static async Task<MeView> MeAsync(string cookie, string from)
    {
        using var client = ClientFor(cookie, from);
        return (await client.GetFromJsonAsync<MeView>(new Uri("/api/me", UriKind.Relative), JsonOptions.Web))!;
    }
}

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Pspsps.Application.Players;
using Pspsps.Domain.Moderation;
using Pspsps.Domain.Safety;
using Pspsps.Infrastructure.Persistence;
using Pspsps.Server.Game;

namespace Pspsps.Integration.Test;

/// <summary>Admins from the configuration, and what their bans, mutes and warnings do, end to end.</summary>
public sealed class ModerationTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
    private static GameFactory Game => PostgresFixture.Game;

    [Test]
    public async Task TheConfiguredAdmins_AreAdmins_AndNobodyElse()
    {
        var (tom, tomCookie) = await Game.SignInGuestAsync();
        Assert.That((await MeAsync(tomCookie)).IsAdmin, Is.False, "nobody is an admin until listed");

        Game.Admins.Add(tom.Id);

        Assert.That((await MeAsync(tomCookie)).IsAdmin, Is.True);
    }

    [Test]
    public async Task OnlyAnAdmin_Moderates()
    {
        var (tom, tomHub) = await Game.ConnectGuestAsync();
        var (felix, felixHub) = await Game.ConnectGuestAsync();
        await using var _ = tomHub;
        await using var __ = felixHub;

        var refused = Assert.ThrowsAsync<HubException>(() =>
            tomHub.InvokeAsync<PlayerCardView>("Moderate", felix.Id, "ban", "Spam", "permanent"));

        Assert.That(refused!.Message, Does.Contain(Moderator.OnlyAdmins));
        Assert.That((await felixHub.InvokeAsync<PlayerCardView>("GetPlayerCard", tom.Id)).Moderation, Is.Null,
            "and a card shows no standing to anyone else");
    }

    [Test]
    public async Task AnAdmin_SeesACatsStandingOnItsCard_AndCantModerateThemselves()
    {
        var (admin, adminHub) = await AdminAsync();
        var (felix, felixHub) = await Game.ConnectGuestAsync();
        await using var _ = adminHub;
        await using var __ = felixHub;

        var card = await adminHub.InvokeAsync<PlayerCardView>("GetPlayerCard", felix.Id);
        Assert.That(card.Moderation, Is.EqualTo(new CardModerationView(false, null, null)));

        var self = Assert.ThrowsAsync<HubException>(() =>
            adminHub.InvokeAsync<PlayerCardView>("Moderate", admin.Id, "mute", "Testing", "1h"));
        Assert.That(self!.Message, Does.Contain("yourself"));
    }

    [Test]
    public async Task ABannedPlayer_IsToldWhy_DroppedAtOnce_AndTheirConnectionRefused_UntilItsLifted()
    {
        var (_, adminHub) = await AdminAsync();
        var (felix, felixCookie) = await Game.SignInGuestAsync();
        var felixHub = await Game.ConnectAsync(felixCookie);
        await using var _ = adminHub;
        await using var __ = felixHub;
        var felixSees = new Recorder(felixHub);
        var closed = new TaskCompletionSource();
        felixHub.Closed += error =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };
        await felixHub.InvokeAsync<RoomSnapshot>("EnterWorld", "siamese");

        var card = await adminHub.InvokeAsync<PlayerCardView>("Moderate", felix.Id, "ban", "Spamming the square", "24h");

        var until = card.Moderation?.Ban?.Until;
        Assert.That(card.Moderation?.Ban?.Reason, Is.EqualTo("Spamming the square"));
        Assert.That(until, Is.EqualTo(Game.ModerationTime.GetUtcNow().AddHours(24)).Within(TimeSpan.FromSeconds(1)));
        Assert.That(await felixSees.Next<SanctionView>("Banned", any => true), Is.EqualTo(new SanctionView("Spamming the square", until)));

        // (A call on it before it closes is refused with why: ModerationHubFilterTests. Over long polling, as here, it closes
        // even sooner: each poll is a request the hub's not-banned policy refuses.)
        Assert.That(await Task.WhenAny(closed.Task, Task.Delay(Wait)), Is.SameAs(closed.Task), "the server closes the connection");

        // Coming back is refused at the door; the page is told why.
        var again = Assert.ThrowsAsync<HttpRequestException>(() => Game.ConnectAsync(felixCookie));
        Assert.That(again!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        var me = await MeAsync(felixCookie);
        Assert.That(me.Ban, Is.EqualTo(new SanctionView("Spamming the square", until)));

        await adminHub.InvokeAsync<PlayerCardView>("Moderate", felix.Id, "lift", null, null);
        await using var back = await Game.ConnectAsync(felixCookie);
        Assert.That((await back.InvokeAsync<RoomSnapshot>("EnterWorld", "siamese")).RoomId, Is.EqualTo("town-square"));
        Assert.That((await MeAsync(felixCookie)).Ban, Is.Null);
    }

    [Test]
    public async Task ABanEnds_AtItsTime()
    {
        var (_, adminHub) = await AdminAsync();
        var (felix, felixCookie) = await Game.SignInGuestAsync();
        await using var _ = adminHub;
        await adminHub.InvokeAsync<PlayerCardView>("Moderate", felix.Id, "ban", "Cooling off", "24h");
        Assert.ThrowsAsync<HttpRequestException>(() => Game.ConnectAsync(felixCookie));

        Game.ModerationTime.Advance(TimeSpan.FromHours(24));

        await using var back = await Game.ConnectAsync(felixCookie);
        Assert.That((await back.InvokeAsync<RoomSnapshot>("EnterWorld", "siamese")).RoomId, Is.EqualTo("town-square"));
    }

    [Test]
    public async Task AMutedPlayer_CantChat_IsToldWhyAndUntilWhen_AndTheMuteEndsAtItsTime()
    {
        var (_, adminHub) = await AdminAsync();
        var (felix, felixCookie) = await Game.SignInGuestAsync();
        var felixHub = await Game.ConnectAsync(felixCookie);
        await using var _ = adminHub;
        await using var __ = felixHub;
        var felixSees = new Recorder(felixHub);
        await felixHub.InvokeAsync<RoomSnapshot>("EnterWorld", "siamese");
        var mutedAt = Game.ModerationTime.GetUtcNow();

        await adminHub.InvokeAsync<PlayerCardView>("Moderate", felix.Id, "mute", "Rude in chat", "1h");

        var told = await felixSees.Next<Recorder.MutedSaid>("Muted", any => true);
        Assert.That(told.Mute, Is.EqualTo(new SanctionView("Rude in chat", mutedAt.AddHours(1))));
        Assert.That((await MeAsync(felixCookie)).Mute, Is.EqualTo(told.Mute));
        var refused = Assert.ThrowsAsync<HubException>(() => felixHub.InvokeAsync("Say", "hello?"));
        Assert.That(refused!.Message, Does.Contain("You're muted until").And.Contain("Rude in chat"));
        // Muted, not banned: the cat still walks.
        await felixHub.InvokeAsync("Move", 1.0, 1.0);

        Game.ModerationTime.Advance(TimeSpan.FromHours(1));

        await felixHub.InvokeAsync("Say", "hello again");
        Assert.That((await MeAsync(felixCookie)).Mute, Is.Null);
    }

    [Test]
    public async Task AWarning_IsShownUntilSeen_AtOnceIfOnline_OrTheNextTime()
    {
        var (_, adminHub) = await AdminAsync();
        var (felix, felixHub) = await Game.ConnectGuestAsync();
        var (tom, tomCookie) = await Game.SignInGuestAsync();
        await using var _ = adminHub;
        await using var __ = felixHub;
        var felixSees = new Recorder(felixHub);

        await adminHub.InvokeAsync<PlayerCardView>("Moderate", felix.Id, "warn", "Please keep it friendly.", null);
        Assert.That(await felixSees.Next<bool>("Warned", any => true), Is.True);
        var warnings = await felixHub.InvokeAsync<List<WarningView>>("GetWarnings");
        Assert.That(warnings.Select(w => w.Text), Is.EqualTo(new[] { "Please keep it friendly." }));
        Assert.That(await felixHub.InvokeAsync<List<WarningView>>("GetWarnings"), Has.Count.EqualTo(1), "until seen");
        await felixHub.InvokeAsync("SawWarning", warnings[0].Id);
        Assert.That(await felixHub.InvokeAsync<List<WarningView>>("GetWarnings"), Is.Empty, "shown until seen, then not again");

        // Tom isn't playing: both wait for him, the first not hidden by the second.
        await adminHub.InvokeAsync<PlayerCardView>("Moderate", tom.Id, "warn", "No spamming.", null);
        Game.ModerationTime.Advance(TimeSpan.FromSeconds(1));
        await adminHub.InvokeAsync<PlayerCardView>("Moderate", tom.Id, "warn", "Really, no spamming.", null);
        await using var tomHub = await Game.ConnectAsync(tomCookie);
        Assert.That((await tomHub.InvokeAsync<List<WarningView>>("GetWarnings")).Select(w => w.Text),
            Is.EqualTo(new[] { "No spamming.", "Really, no spamming." }));
    }

    [Test]
    public async Task TheAdminPage_IsForAdminsOnly_AndListsWhoIsBannedOrMuted()
    {
        var (admin, adminHub) = await AdminAsync();
        await using var _ = adminHub;
        var felix = (await Game.SignInGuestAsync()).Player;
        var (tom, tomCookie) = await Game.SignInGuestAsync();
        await adminHub.InvokeAsync<PlayerCardView>("Moderate", felix.Id, "ban", "Spam", "permanent");

        using var anonymous = Game.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using (var page = await anonymous.GetAsync(new Uri("/admin", UriKind.Relative)))
        {
            Assert.That(page.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }

        using var asTom = ClientFor(tomCookie);
        using (var page = await asTom.GetAsync(new Uri("/admin", UriKind.Relative)))
        {
            Assert.That(page.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(await page.Content.ReadAsStringAsync(), Does.Contain("Not an admin").And.Contain(tom.Id.ToString()),
                "a player who isn't an admin is shown their player ID, for the admin list");
        }

        using (var overview = await asTom.GetAsync(new Uri("/api/admin/overview", UriKind.Relative)))
        {
            Assert.That(overview.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        }

        using var asAdmin = ClientFor(AdminCookies[admin.Id]);
        using (var page = await asAdmin.GetAsync(new Uri("/admin", UriKind.Relative)))
        {
            // The page itself is built by the client, which these tests don't build.
            Assert.That(page.StatusCode, Is.AnyOf(HttpStatusCode.OK, HttpStatusCode.NotFound));
        }

        var view = await asAdmin.GetFromJsonAsync<AdminOverviewView>(new Uri("/api/admin/overview", UriKind.Relative), JsonOptions.Web);
        var banned = view!.Sanctioned.Single(s => s.PlayerId == felix.Id);
        Assert.That(banned.DisplayName, Is.EqualTo(felix.DisplayName));
        Assert.That(banned.Ban, Has.Property(nameof(AdminSanctionView.Reason)).EqualTo("Spam")
            .And.Property(nameof(AdminSanctionView.Until)).Null
            .And.Property(nameof(AdminSanctionView.ByName)).EqualTo(admin.DisplayName));
        Assert.That(view.Recent, Has.Some.Matches<ModerationLogView>(line =>
            line.PlayerId == felix.Id && line.Kind == "Banned" && line.ById == admin.Id && line.Reason == "Spam"));
    }

    [Test]
    public async Task TheAdminPage_Lifts_FromThisSiteOnly()
    {
        var (admin, adminHub) = await AdminAsync();
        await using var _ = adminHub;
        var felix = (await Game.SignInGuestAsync()).Player;
        await adminHub.InvokeAsync<PlayerCardView>("Moderate", felix.Id, "mute", "Rude", "7d");
        using var asAdmin = ClientFor(AdminCookies[admin.Id]);
        var lift = new Uri($"/api/admin/players/{felix.Id}/moderate", UriKind.Relative);

        using (var crossSite = new HttpRequestMessage(HttpMethod.Post, lift) { Content = JsonContent.Create(new { action = "lift" }) })
        {
            crossSite.Headers.Add("Sec-Fetch-Site", "cross-site");
            using var refused = await asAdmin.SendAsync(crossSite);
            Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        }

        using (var nothing = new HttpRequestMessage(HttpMethod.Post, lift) { Content = JsonContent.Create(new { action = "dance" }) })
        {
            nothing.Headers.Add("Sec-Fetch-Site", "same-origin");
            using var refused = await asAdmin.SendAsync(nothing);
            Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        }

        using var sameSite = new HttpRequestMessage(HttpMethod.Post, lift) { Content = JsonContent.Create(new { action = "lift" }) };
        sameSite.Headers.Add("Sec-Fetch-Site", "same-origin");
        using var lifted = await asAdmin.SendAsync(sameSite);
        Assert.That(lifted.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await lifted.Content.ReadFromJsonAsync<CardModerationView>(JsonOptions.Web),
            Is.EqualTo(new CardModerationView(false, null, null)));
    }

    [Test]
    public async Task TheAdminPage_ListsOpenReportsWithTheirChat_AndActingOnOneClosesItWithWhatWasDone()
    {
        var (admin, adminHub) = await AdminAsync();
        var (tom, tomHub) = await Game.ConnectGuestAsync();
        var (felix, felixHub) = await Game.ConnectGuestAsync();
        await using var _ = adminHub;
        await using var __ = tomHub;
        await using var ___ = felixHub;
        await tomHub.InvokeAsync<RoomSnapshot>("EnterWorld", "tabby");
        await felixHub.InvokeAsync<RoomSnapshot>("EnterWorld", "tabby");
        await felixHub.InvokeAsync("Say", "your hat is silly");
        await tomHub.InvokeAsync<ReportReceipt>("Report", felix.Id, "MeanChat", "rude to me", false);
        using var asAdmin = ClientFor(AdminCookies[admin.Id]);

        var report = (await OverviewAsync(asAdmin)).Reports.Single(r => r.ReportedId == felix.Id);
        Assert.Multiple(() =>
        {
            Assert.That((report.ReporterId, report.ReporterName), Is.EqualTo((tom.Id, tom.DisplayName)));
            Assert.That((report.ReportedName, report.Reason, report.Note), Is.EqualTo((felix.DisplayName, "MeanChat", "rude to me")));
            Assert.That(report.Place, Is.EqualTo("Town Square, Tabby"));
            Assert.That(report.Context, Has.Some.Matches<ReportLineView>(line =>
                line.PlayerId == felix.Id && line.DisplayName == felix.DisplayName && line.Text == "your hat is silly"));
        });

        using (var banned = await DecideAsync(asAdmin, report.Id, new { action = "ban", reason = "Mean in chat", length = "24h" }))
        {
            Assert.That(banned.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        }

        var after = await OverviewAsync(asAdmin);
        Assert.That(after.Reports.Select(r => r.Id), Does.Not.Contain(report.Id), "closed, it leaves the queue");
        Assert.That(after.Sanctioned.Single(s => s.PlayerId == felix.Id).Ban?.Reason, Is.EqualTo("Mean in chat"));
        var closed = Game.Get<PspspsDbContext>().Reports.Single(r => r.Id == report.Id);
        Assert.That((closed.Status, closed.Decision, closed.ClosedBy),
            Is.EqualTo((ReportStatus.Closed, (ReportDecision?)ReportDecision.Banned, (Guid?)admin.Id)));

        using var again = await DecideAsync(asAdmin, report.Id, new { action = "dismiss" });
        Assert.That(again.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "decided once");
    }

    [Test]
    public async Task AReportDismissed_LeavesTheQueue_AndThePlayerAlone_AndOnlyAnAdminDecides()
    {
        var (admin, adminHub) = await AdminAsync();
        var tomCookie = (await Game.SignInGuestAsync()).Cookie;
        var tomHub = await Game.ConnectAsync(tomCookie);
        var (felix, felixHub) = await Game.ConnectGuestAsync();
        await using var _ = adminHub;
        await using var __ = tomHub;
        await using var ___ = felixHub;
        await tomHub.InvokeAsync<RoomSnapshot>("EnterWorld", "calico");
        await felixHub.InvokeAsync<RoomSnapshot>("EnterWorld", "calico");
        await tomHub.InvokeAsync<ReportReceipt>("Report", felix.Id, "Spam", null, false);
        using var asAdmin = ClientFor(AdminCookies[admin.Id]);
        var report = (await OverviewAsync(asAdmin)).Reports.Single(r => r.ReportedId == felix.Id);

        using var asTom = ClientFor(tomCookie);
        using (var refused = await DecideAsync(asTom, report.Id, new { action = "dismiss" }))
        {
            Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        }

        using (var dismissed = await DecideAsync(asAdmin, report.Id, new { action = "dismiss" }))
        {
            Assert.That(dismissed.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        }

        var after = await OverviewAsync(asAdmin);
        Assert.That(after.Reports.Select(r => r.Id), Does.Not.Contain(report.Id));
        Assert.That(after.Sanctioned.Select(s => s.PlayerId), Does.Not.Contain(felix.Id));
        Assert.That(Game.Get<PspspsDbContext>().Reports.Single(r => r.Id == report.Id).Decision, Is.EqualTo(ReportDecision.Dismissed));
    }

    private static async Task<AdminOverviewView> OverviewAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<AdminOverviewView>(new Uri("/api/admin/overview", UriKind.Relative), JsonOptions.Web))!;

    private static async Task<HttpResponseMessage> DecideAsync(HttpClient client, Guid reportId, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/api/admin/reports/{reportId}/decide", UriKind.Relative))
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("Sec-Fetch-Site", "same-origin");
        return await client.SendAsync(request);
    }

    // Each admin's cookie, by player ID, for the admin page.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, string> AdminCookies = new();

    /// <summary>A new guest, made an admin through the configuration, with their hub connection.</summary>
    private static async Task<(PlayerProfile Player, HubConnection Hub)> AdminAsync()
    {
        var (player, cookie) = await Game.SignInGuestAsync();
        Game.Admins.Add(player.Id);
        AdminCookies[player.Id] = cookie;
        return (player, await Game.ConnectAsync(cookie));
    }

    private static HttpClient ClientFor(string cookie)
    {
        var client = Game.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return client;
    }

    private static async Task<MeView> MeAsync(string cookie)
    {
        using var client = ClientFor(cookie);
        return (await client.GetFromJsonAsync<MeView>(new Uri("/api/me", UriKind.Relative), JsonOptions.Web))!;
    }
}

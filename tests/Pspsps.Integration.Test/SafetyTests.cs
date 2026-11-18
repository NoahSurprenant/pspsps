using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Pspsps.Application.Safety;
using Pspsps.Domain;
using Pspsps.Domain.Players;
using Pspsps.Domain.Rooms;
using Pspsps.Domain.Safety;
using Pspsps.Infrastructure.Persistence;
using Pspsps.Server.Game;

namespace Pspsps.Integration.Test;

/// <summary>Mutes and reports, end to end: guests on the hub, the room grains and PostgreSQL.</summary>
public sealed class SafetyTests
{
    private static GameFactory Game => PostgresFixture.Game;

    [Test]
    public async Task AReport_KeepsTheRoomsOwnRecentLines_WithWhoSaidThem()
    {
        var (tom, tomHub) = await Game.ConnectGuestAsync();
        var (felix, felixHub) = await Game.ConnectGuestAsync();
        await using var _ = tomHub;
        await using var __ = felixHub;
        var tomHears = new Recorder(tomHub);
        await tomHub.InvokeAsync<RoomSnapshot>("EnterWorld", "siamese");
        await tomHub.InvokeAsync<RoomSnapshot>("GoToRoom", "cafe");
        await felixHub.InvokeAsync<RoomSnapshot>("EnterWorld", "siamese");
        await felixHub.InvokeAsync<RoomSnapshot>("GoToRoom", "cafe");

        // Felix says two things, the first through the chat filter ("heck" is masked); Tom says one back.
        await felixHub.InvokeAsync("Say", "what the heck");
        await felixHub.InvokeAsync("Say", "go away");
        await tomHub.InvokeAsync("Say", "rude");
        await tomHears.Next<ChatView>("ChatSaid", line => line.PlayerId == tom.Id);

        var receipt = await tomHub.InvokeAsync<ReportReceipt>("Report", felix.Id, "MeanChat", "  was mean  ", false);

        Assert.That(receipt.Muted, Is.False);
        var db = Game.Get<PspspsDbContext>();
        var report = await db.Reports.AsNoTracking().SingleAsync(r => r.ReporterId == tom.Id);
        Assert.Multiple(() =>
        {
            Assert.That(report.ReportedId, Is.EqualTo(felix.Id));
            Assert.That(report.ReportedName, Is.EqualTo(felix.DisplayName));
            Assert.That(report.Reason, Is.EqualTo(ReportReason.MeanChat));
            Assert.That(report.Note, Is.EqualTo("was mean"));
            Assert.That((report.WorldId, report.Room), Is.EqualTo(("siamese", "siamese/cafe")));
            Assert.That(report.Status, Is.EqualTo(ReportStatus.Open));
        });
        // The room's lines as the room has them (filtered, with each speaker's name), the latest last. The café may have
        // older lines from other tests before them.
        var lastThree = report.Context.TakeLast(3).Select(l => (l.PlayerId, l.DisplayName, l.Text)).ToList();
        Assert.That(lastThree, Is.EqualTo(new[]
        {
            (felix.Id, felix.DisplayName, "what the ****"),
            (felix.Id, felix.DisplayName, "go away"),
            (tom.Id, tom.DisplayName, "rude"),
        }));
        Assert.That(report.Context.Select(l => l.Position), Is.EqualTo(Enumerable.Range(0, report.Context.Count)));
        Assert.That(report.Context, Has.Count.LessThanOrEqualTo(Report.MaxContextLines));
    }

    [Test]
    public async Task AReport_OfACatInAnotherRoom_WhoSaidNothingHere_KeepsNoLines()
    {
        var (tom, tomHub) = await Game.ConnectGuestAsync();
        var (felix, felixHub) = await Game.ConnectGuestAsync();
        await using var _ = tomHub;
        await using var __ = felixHub;
        var tomHears = new Recorder(tomHub);
        await tomHub.InvokeAsync<RoomSnapshot>("EnterWorld", "siamese");
        await felixHub.InvokeAsync<RoomSnapshot>("EnterWorld", "calico");
        await tomHub.InvokeAsync("Say", "nice day");
        await tomHears.Next<ChatView>("ChatSaid", line => line.PlayerId == tom.Id);

        // Felix's card, opened from the friends list, say: nothing said in Tom's room is about him.
        await tomHub.InvokeAsync<ReportReceipt>("Report", felix.Id, "InappropriateName", null, false);

        var report = await Game.Get<PspspsDbContext>().Reports.AsNoTracking().SingleAsync(r => r.ReporterId == tom.Id);
        Assert.That(report.Context, Is.Empty);
        Assert.That(report.Room, Is.EqualTo("siamese/town-square"));
    }

    [Test]
    public async Task AReport_OfTheSameCatTwiceInAnHour_IsRefused_AndYourselfToo()
    {
        var (tom, tomHub) = await Game.ConnectGuestAsync();
        var (felix, _) = await Game.SignInGuestAsync();
        await using var _ = tomHub;
        await tomHub.InvokeAsync<RoomSnapshot>("EnterWorld", "siamese");

        await tomHub.InvokeAsync<ReportReceipt>("Report", felix.Id, "Spam", null, false);
        var twice = Assert.ThrowsAsync<HubException>(() => tomHub.InvokeAsync<ReportReceipt>("Report", felix.Id, "Spam", null, false));
        var self = Assert.ThrowsAsync<HubException>(() => tomHub.InvokeAsync<ReportReceipt>("Report", tom.Id, "Spam", null, false));

        Assert.That(twice!.Message, Does.Contain("You've reported that cat already."));
        Assert.That(self!.Message, Does.Contain("You can't report yourself."));
        var db = Game.Get<PspspsDbContext>();
        Assert.That(await db.Reports.CountAsync(r => r.ReporterId == tom.Id), Is.EqualTo(1));
    }

    [Test]
    public async Task AReport_AlsoMuting_MutesTheCat_InTheSameSave()
    {
        var (tom, tomHub) = await Game.ConnectGuestAsync();
        var (felix, _) = await Game.SignInGuestAsync();
        await using var _ = tomHub;
        await tomHub.InvokeAsync<RoomSnapshot>("EnterWorld", "siamese");

        var receipt = await tomHub.InvokeAsync<ReportReceipt>("Report", felix.Id, "InappropriateName", null, true);

        Assert.That(receipt.Muted, Is.True);
        Assert.That(receipt.Mutes.Select(m => m.PlayerId), Is.EqualTo(new[] { felix.Id }));
        var mutes = await Game.Get<IMuteService>().ListAsync(tom.Id, CancellationToken.None);
        Assert.That(mutes.Select(m => m.PlayerId), Is.EqualTo(new[] { felix.Id }));
    }

    [Test]
    public async Task AReportRefused_MutesNobody()
    {
        var tom = await NewPlayerAsync();
        var felix = await NewPlayerAsync();

        var refused = Assert.ThrowsAsync<DomainException>(() => Game.Get<IReportService>().FileAsync(
            tom, felix, ReportReason.Spam, new string('a', Report.MaxNoteLength + 1), "tabby", "tabby/town-square", [], false, true,
            CancellationToken.None));

        Assert.That(refused!.Message, Does.Contain("A note is at most"));
        Assert.That(await Game.Get<IMuteService>().ListAsync(tom, CancellationToken.None), Is.Empty);
    }

    [Test]
    public async Task AReport_OfALineByACatThatIsGone_SaysACatSaidIt()
    {
        var tom = await NewPlayerAsync();
        var felix = await NewPlayerAsync();
        var now = DateTimeOffset.UtcNow;

        await Game.Get<IReportService>().FileAsync(
            tom, felix, ReportReason.SomethingElse, null, "tabby", "tabby/town-square",
            [new ChatLine(felix, "hi", now.AddSeconds(-2)), new ChatLine(Guid.NewGuid(), "bye", now.AddSeconds(-1))], false, false,
            CancellationToken.None);

        var db = Game.Get<PspspsDbContext>();
        var report = await db.Reports.AsNoTracking().SingleAsync(r => r.ReporterId == tom);
        Assert.That(report.Context.Select(l => (l.Text, l.DisplayName)), Is.EqualTo(new[]
        {
            ("hi", (await db.Players.SingleAsync(p => p.Id == felix)).DisplayName),
            ("bye", "A cat"),
        }));
    }

    [Test]
    public async Task AReport_AlsoMuting_WithEveryMuteUsed_IsStillFiled_WithoutTheMute()
    {
        var tom = await NewPlayerAsync();
        var felix = await NewPlayerAsync();
        var db = Game.Get<PspspsDbContext>();
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < Mute.MaxMutes; i++)
        {
            var other = Player.CreateGuest($"Muted {i}", FurColor.Grey, now);
            db.Players.Add(other);
            db.Mutes.Add(Mute.Create(tom, other.Id, new MutesSoFar(i, false), now));
        }

        await db.SaveChangesAsync();

        var outcome = await Game.Get<IReportService>().FileAsync(
            tom, felix, ReportReason.MeanChat, null, "tabby", "tabby/town-square", [], false, true, CancellationToken.None);

        Assert.That(outcome.Muted, Is.False, "the report matters more than the mute");
        Assert.That(await Game.Get<PspspsDbContext>().Reports.CountAsync(r => r.ReporterId == tom), Is.EqualTo(1));
    }

    [Test]
    public async Task AReport_OutlastsTheReportedPlayer()
    {
        var tom = await NewPlayerAsync();
        var felix = await NewPlayerAsync();
        await Game.Get<IReportService>().FileAsync(
            tom, felix, ReportReason.InappropriateName, null, "tabby", "tabby/town-square", [], false, false, CancellationToken.None);

        var db = Game.Get<PspspsDbContext>();
        db.Players.Remove(await db.Players.SingleAsync(p => p.Id == felix));
        await db.SaveChangesAsync();

        var report = await Game.Get<PspspsDbContext>().Reports.AsNoTracking().SingleAsync(r => r.ReporterId == tom);
        Assert.That(report.ReportedId, Is.EqualTo(felix), "the report stays for review, with the name the cat had");
    }

    [Test]
    public async Task AMute_IsKept_ToldToThePlayersOtherTabs_AndUndone()
    {
        var (tom, cookie) = await Game.SignInGuestAsync();
        var (felix, _) = await Game.SignInGuestAsync();
        await using var tab = await Game.ConnectAsync(cookie);
        await using var otherTab = await Game.ConnectAsync(cookie);
        var otherTabHears = new Recorder(otherTab);

        var mutes = await tab.InvokeAsync<List<MutedView>>("Mute", felix.Id);

        var muted = new MutedView(felix.Id, felix.DisplayName, felix.FurColor.ToString());
        Assert.That(mutes, Is.EqualTo(new[] { muted }));
        var told = await otherTabHears.Next<List<MutedView>>("MutesChanged", _ => true);
        Assert.That(told, Is.EqualTo(new[] { muted }));

        // A later connection (a reload, another device) gets the list.
        await using (var later = await Game.ConnectAsync(cookie))
        {
            Assert.That(await later.InvokeAsync<List<MutedView>>("GetMutes"), Is.EqualTo(new[] { muted }));
        }

        var twice = Assert.ThrowsAsync<HubException>(() => tab.InvokeAsync<List<MutedView>>("Mute", felix.Id));
        Assert.That(twice!.Message, Does.Contain("You've muted that cat already."));
        var self = Assert.ThrowsAsync<HubException>(() => tab.InvokeAsync<List<MutedView>>("Mute", tom.Id));
        Assert.That(self!.Message, Does.Contain("You can't mute yourself."));

        Assert.That(await tab.InvokeAsync<List<MutedView>>("Unmute", felix.Id), Is.Empty);
        Assert.That(await otherTabHears.Next<List<MutedView>>("MutesChanged", list => list.Count == 0), Is.Empty);
        Assert.That(await tab.InvokeAsync<List<MutedView>>("GetMutes"), Is.Empty);
        var again = Assert.ThrowsAsync<HubException>(() => tab.InvokeAsync<List<MutedView>>("Unmute", felix.Id));
        Assert.That(again!.Message, Does.Contain("You haven't muted that cat."));
    }

    [Test]
    public async Task AMuteOfNoSuchCat_IsRefused()
    {
        var tom = await NewPlayerAsync();

        var refused = Assert.ThrowsAsync<DomainException>(
            () => Game.Get<IMuteService>().MuteAsync(tom, Guid.NewGuid(), CancellationToken.None));

        Assert.That(refused!.Message, Is.EqualTo("That cat doesn't exist."));
    }

    private static async Task<Guid> NewPlayerAsync() =>
        (await Game.Get<Pspsps.Application.Players.IPlayerService>().CreateGuestAsync(CancellationToken.None)).Id;
}

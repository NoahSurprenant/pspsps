using Pspsps.Domain.Safety;

namespace Pspsps.Domain.Test;

public sealed class ReportTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Tom = Guid.NewGuid();
    private static readonly Guid Felix = Guid.NewGuid();
    private static readonly Guid Garfield = Guid.NewGuid();

    private static Report File(
        Guid reported,
        ReportReason reason = ReportReason.MeanChat,
        string? note = null,
        IEnumerable<ChatContextLine>? chat = null,
        IEnumerable<PastReport>? past = null,
        Guid? reporter = null,
        string worldId = "tabby",
        string room = "tabby/town-square",
        bool reportedIsHere = true) =>
        Report.File(reporter ?? Tom, reported, "Felix", reason, note, worldId, room, chat ?? [], reportedIsHere, past ?? [], Now);

    [Test]
    public void File_KeepsWhoWhyWhereAndWhen_AndIsOpen()
    {
        var report = File(Felix, ReportReason.Spam, "  keeps saying the same thing  ");

        Assert.Multiple(() =>
        {
            Assert.That(report.Id, Is.Not.EqualTo(Guid.Empty));
            Assert.That(report.ReporterId, Is.EqualTo(Tom));
            Assert.That(report.ReportedId, Is.EqualTo(Felix));
            Assert.That(report.ReportedName, Is.EqualTo("Felix"));
            Assert.That(report.Reason, Is.EqualTo(ReportReason.Spam));
            Assert.That(report.Note, Is.EqualTo("keeps saying the same thing"), "trimmed");
            Assert.That((report.WorldId, report.Room), Is.EqualTo(("tabby", "tabby/town-square")));
            Assert.That(report.FiledAt, Is.EqualTo(Now));
            Assert.That(report.Status, Is.EqualTo(ReportStatus.Open));
        });
    }

    [Test]
    public void File_ABlankNote_IsNone()
    {
        Assert.That(File(Felix, note: "   ").Note, Is.Null);
    }

    [Test]
    public void File_ANoteUpToTheLimit_IsKept_ALongerOne_Refused()
    {
        Assert.That(File(Felix, note: new string('a', Report.MaxNoteLength)).Note, Has.Length.EqualTo(Report.MaxNoteLength));

        var refused = Assert.Throws<DomainException>(() => File(Felix, note: new string('a', Report.MaxNoteLength + 1)));
        Assert.That(refused!.Message, Is.EqualTo($"A note is at most {Report.MaxNoteLength} characters."));
    }

    [Test]
    public void File_Yourself_IsRefused()
    {
        var refused = Assert.Throws<DomainException>(() => File(Tom));

        Assert.That(refused!.Message, Is.EqualTo("You can't report yourself."));
    }

    [Test]
    public void File_EveryReasonOnTheList_IsTaken_AnythingElse_Refused()
    {
        foreach (var reason in Enum.GetValues<ReportReason>())
        {
            Assert.That(File(Felix, reason).Reason, Is.EqualTo(reason));
        }

        var refused = Assert.Throws<DomainException>(() => File(Felix, (ReportReason)42));
        Assert.That(refused!.Message, Is.EqualTo("Pick a reason."));
    }

    [Test]
    public void File_FromNoRoom_IsRefused()
    {
        Assert.Throws<DomainException>(() => File(Felix, room: ""));
        Assert.Throws<DomainException>(() => File(Felix, worldId: " "));
    }

    [Test]
    public void File_KeepsTheRoomsLastLines_OldestFirst()
    {
        var chat = Enumerable.Range(0, Report.MaxContextLines + 5)
            .Select(i => new ChatContextLine(i % 2 == 0 ? Felix : Garfield, i % 2 == 0 ? "Felix" : "Garfield", $"line {i}", Now.AddSeconds(i - 60)))
            .ToList();

        var report = File(Felix, chat: chat);

        Assert.That(report.Context.Select(l => l.Text), Is.EqualTo(chat.Skip(5).Select(l => l.Text)));
        Assert.That(report.Context.Select(l => l.Position), Is.EqualTo(Enumerable.Range(0, Report.MaxContextLines)));
        var first = report.Context[0];
        Assert.That((first.PlayerId, first.DisplayName, first.At), Is.EqualTo((Garfield, "Garfield", Now.AddSeconds(-55))));
    }

    [Test]
    public void File_TheSameCatAgainWithinTheWindow_WhileOpen_IsRefused()
    {
        PastReport[] past = [new(Felix, Now.AddMinutes(-59), ReportStatus.Open)];

        var refused = Assert.Throws<DomainException>(() => File(Felix, past: past));

        Assert.That(refused!.Message, Does.Contain("You've reported that cat already."));
    }

    [Test]
    public void File_TheSameCatAgainAfterTheWindow_OrAnotherCat_IsTaken()
    {
        Assert.DoesNotThrow(() => File(Felix, past: [new(Felix, Now - Report.Window, ReportStatus.Open)]));
        Assert.DoesNotThrow(() => File(Garfield, past: [new(Felix, Now.AddMinutes(-1), ReportStatus.Open)]));
    }

    [Test]
    public void File_MoreThanTheLimitWithinTheWindow_IsRefused()
    {
        var past = Enumerable.Range(0, Report.MaxPerWindow - 1)
            .Select(i => new PastReport(Guid.NewGuid(), Now.AddMinutes(-i - 1), ReportStatus.Open))
            .ToList();
        Assert.DoesNotThrow(() => File(Felix, past: past));

        past.Add(new PastReport(Guid.NewGuid(), Now.AddMinutes(-30), ReportStatus.Open));
        var refused = Assert.Throws<DomainException>(() => File(Felix, past: past));
        Assert.That(refused!.Message, Is.EqualTo("You've sent a lot of reports lately. Try again later."));

        // Reports older than the window don't count.
        var old = past.Select(r => r with { FiledAt = Now - Report.Window - TimeSpan.FromSeconds(1) });
        Assert.DoesNotThrow(() => File(Felix, past: old));
    }

    [Test]
    public void File_ACatNotInTheRoom_WhoSaidNothingThere_IsReportedWithNoLines()
    {
        ChatContextLine[] strangers = [new(Garfield, "Garfield", "nice day", Now.AddSeconds(-5))];

        var report = File(Felix, chat: strangers, reportedIsHere: false);

        Assert.That(report.Context, Is.Empty, "not what strangers said, from a card opened elsewhere");
    }

    [Test]
    public void File_ACatNotInTheRoomAnyMore_WhoSaidSomethingThere_IsReportedWithTheLines()
    {
        ChatContextLine[] chat =
        [
            new(Felix, "Felix", "go away", Now.AddSeconds(-10)),
            new(Garfield, "Garfield", "that was mean", Now.AddSeconds(-5)),
        ];

        var report = File(Felix, chat: chat, reportedIsHere: false);

        Assert.That(report.Context.Select(l => l.Text), Is.EqualTo(new[] { "go away", "that was mean" }));
    }
}

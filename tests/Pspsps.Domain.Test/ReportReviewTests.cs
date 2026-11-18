using Pspsps.Domain.Moderation;
using Pspsps.Domain.Safety;

namespace Pspsps.Domain.Test;

/// <summary>An admin reviewing a report: dismissed, or closed with what was done to the reported player.</summary>
public sealed class ReportReviewTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Admin = Guid.NewGuid();
    private static readonly Guid Tom = Guid.NewGuid();
    private static readonly Guid Felix = Guid.NewGuid();
    private static Moderator Moderator => Moderator.Of(Admin, new HashSet<Guid> { Admin });

    private static Report Filed(Guid? reported = null) =>
        Report.File(Tom, reported ?? Felix, "Felix", ReportReason.MeanChat, null, "tabby", "tabby/town-square", [], true, [], Now);

    [Test]
    public void Dismiss_ClosesTheReport_SayingWhoAndWhen()
    {
        var report = Filed();

        report.Dismiss(Moderator, Now.AddHours(1));

        Assert.Multiple(() =>
        {
            Assert.That(report.Status, Is.EqualTo(ReportStatus.Closed));
            Assert.That(report.Decision, Is.EqualTo(ReportDecision.Dismissed));
            Assert.That(report.ClosedBy, Is.EqualTo(Admin));
            Assert.That(report.ClosedAt, Is.EqualTo(Now.AddHours(1)));
        });
    }

    [TestCase(ReportDecision.Warned)]
    [TestCase(ReportDecision.Muted)]
    [TestCase(ReportDecision.Banned)]
    public void CloseWith_KeepsWhatWasDone(ReportDecision decision)
    {
        var report = Filed();

        report.CloseWith(Moderator, decision, Now.AddHours(1));

        Assert.That((report.Status, report.Decision, report.ClosedBy), Is.EqualTo((ReportStatus.Closed, (ReportDecision?)decision, (Guid?)Admin)));
    }

    [Test]
    public void AReport_IsDecidedOnce()
    {
        var report = Filed();
        report.Dismiss(Moderator, Now);

        var refused = Assert.Throws<DomainException>(() => report.CloseWith(Moderator, ReportDecision.Banned, Now));
        Assert.That(refused!.Message, Does.Contain("already"));
        Assert.Throws<DomainException>(() => report.Dismiss(Moderator, Now));
        Assert.That(report.Decision, Is.EqualTo(ReportDecision.Dismissed));
    }

    [Test]
    public void AnAdmin_DoesntReviewAReportAboutThemselves()
    {
        var aboutAdmin = Filed(reported: Admin);

        Assert.Throws<DomainException>(() => aboutAdmin.Dismiss(Moderator, Now));
        Assert.That(aboutAdmin.Status, Is.EqualTo(ReportStatus.Open));
    }

    [Test]
    public void Dismiss_IsNotAnActionTaken()
    {
        Assert.Throws<DomainException>(() => Filed().CloseWith(Moderator, ReportDecision.Dismissed, Now));
        Assert.Throws<DomainException>(() => Filed().CloseWith(Moderator, (ReportDecision)42, Now));
    }
}

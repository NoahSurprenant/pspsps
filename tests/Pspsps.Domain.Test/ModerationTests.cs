using Pspsps.Domain.Moderation;

namespace Pspsps.Domain.Test;

public sealed class ModerationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Admin = Guid.NewGuid();
    private static readonly Guid OtherAdmin = Guid.NewGuid();
    private static readonly Guid Felix = Guid.NewGuid();
    private static readonly IReadOnlySet<Guid> Admins = new HashSet<Guid> { Admin, OtherAdmin };

    private static Moderator Moderator => Moderator.Of(Admin, Admins);

    [Test]
    public void OnlyAnAdminIsAModerator()
    {
        Assert.That(Moderator.Of(Admin, Admins).PlayerId, Is.EqualTo(Admin));
        var refused = Assert.Throws<DomainException>(() => Moderator.Of(Felix, Admins));
        Assert.That(refused!.Message, Is.EqualTo(Moderator.OnlyAdmins));
        Assert.Throws<DomainException>(() => Moderator.Of(Guid.Empty, new HashSet<Guid> { Guid.Empty }));
    }

    [Test]
    public void ANewPlayer_IsNeitherBannedNorMuted()
    {
        var felix = PlayerModeration.For(Felix);

        Assert.Multiple(() =>
        {
            Assert.That(felix.BanAt(Now), Is.Null);
            Assert.That(felix.MuteAt(Now), Is.Null);
            Assert.That(felix.Warning, Is.Null);
            Assert.That(felix.Entries, Is.Empty);
        });
        Assert.Throws<DomainException>(() => PlayerModeration.For(Guid.Empty));
    }

    [Test]
    public void Ban_HoldsUntilItsTime_ThenEndsByItself()
    {
        var felix = PlayerModeration.For(Felix);
        var until = Now.AddHours(24);

        felix.BanUntil(Moderator, "  Spamming the square  ", until, Now);

        var ban = felix.BanAt(Now.AddHours(23));
        Assert.That(ban, Is.EqualTo(new Sanction("Spamming the square", Now, until, Admin)));
        Assert.That(ban!.IsPermanent, Is.False);
        Assert.That(felix.BanAt(until.AddTicks(-1)), Is.Not.Null, "in force up to its end");
        Assert.That(felix.BanAt(until), Is.Null, "and not a moment after");
        Assert.That(felix.MuteAt(Now), Is.Null, "a ban isn't a mute");
    }

    [Test]
    public void Ban_WithNoEnd_IsForGood()
    {
        var felix = PlayerModeration.For(Felix);

        felix.BanUntil(Moderator, "Threats", until: null, Now);

        Assert.That(felix.BanAt(Now.AddYears(50))?.IsPermanent, Is.True);
        Assert.That(felix.BanAt(Now)!.SaysBanned(), Is.EqualTo("Your cat is banned for good. Why: Threats"));
    }

    [Test]
    public void Mute_HoldsUntilItsTime_AndSaysWhyAndUntilWhen()
    {
        var felix = PlayerModeration.For(Felix);

        felix.MuteUntil(Moderator, "Rude in chat", Now.AddHours(1), Now);

        Assert.That(felix.MuteAt(Now.AddMinutes(59))?.Reason, Is.EqualTo("Rude in chat"));
        Assert.That(felix.MuteAt(Now.AddHours(1)), Is.Null);
        Assert.That(felix.BanAt(Now), Is.Null, "a mute isn't a ban");
        Assert.That(felix.MuteAt(Now)!.SaysMuted(), Is.EqualTo("You're muted until 18 Nov 2026 13:00 UTC. Why: Rude in chat"));
    }

    [Test]
    public void AMuteOrBan_EndsLaterThanNow()
    {
        var felix = PlayerModeration.For(Felix);

        Assert.Throws<DomainException>(() => felix.MuteUntil(Moderator, "Rude", Now, Now));
        Assert.Throws<DomainException>(() => felix.BanUntil(Moderator, "Rude", Now.AddSeconds(-1), Now));
        Assert.That(felix.Entries, Is.Empty);
    }

    [Test]
    public void ANewBanOrMute_TakesThePlaceOfTheLast()
    {
        var felix = PlayerModeration.For(Felix);
        felix.MuteUntil(Moderator, "Rude", Now.AddDays(7), Now);

        felix.MuteUntil(Moderator, "Rude, but sorry now", Now.AddHours(1), Now.AddMinutes(5));

        Assert.That(felix.MuteAt(Now.AddHours(2)), Is.Null, "the shorter mute is the one in force");
        Assert.That(felix.MuteAt(Now.AddMinutes(10))?.Reason, Is.EqualTo("Rude, but sorry now"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void EveryAction_SaysWhy(string? reason)
    {
        var felix = PlayerModeration.For(Felix);

        Assert.Multiple(() =>
        {
            Assert.Throws<DomainException>(() => felix.Warn(Moderator, reason!, Now));
            Assert.Throws<DomainException>(() => felix.MuteUntil(Moderator, reason!, Now.AddHours(1), Now));
            Assert.Throws<DomainException>(() => felix.BanUntil(Moderator, reason!, null, Now));
        });
        Assert.That(felix.Entries, Is.Empty);
    }

    [Test]
    public void AReason_IsKeptShort()
    {
        var felix = PlayerModeration.For(Felix);

        felix.Warn(Moderator, new string('a', PlayerModeration.MaxReasonLength), Now);
        Assert.Throws<DomainException>(() =>
            felix.Warn(Moderator, new string('a', PlayerModeration.MaxReasonLength + 1), Now));
    }

    [Test]
    public void NobodyModeratesThemselves()
    {
        var admin = PlayerModeration.For(Admin);

        var refused = Assert.Throws<DomainException>(() => admin.BanUntil(Moderator, "Testing", null, Now));
        Assert.That(refused!.Message, Is.EqualTo("You can't moderate yourself."));
        Assert.Throws<DomainException>(() => admin.MuteUntil(Moderator, "Testing", Now.AddHours(1), Now));
        Assert.Throws<DomainException>(() => admin.Warn(Moderator, "Testing", Now));
        Assert.That(admin.BanAt(Now), Is.Null);
    }

    [Test]
    public void AdminsAreNotModerated_TheListChangesThem()
    {
        var other = PlayerModeration.For(OtherAdmin);

        var refused = Assert.Throws<DomainException>(() => other.BanUntil(Moderator, "Rogue", null, Now));
        Assert.That(refused!.Message, Does.Contain("admin"));
        Assert.That(other.BanAt(Now), Is.Null);
    }

    [Test]
    public void Lift_EndsTheBanAndTheMute()
    {
        var felix = PlayerModeration.For(Felix);
        felix.BanUntil(Moderator, "Spam", Now.AddDays(7), Now);
        felix.MuteUntil(Moderator, "Spam", Now.AddHours(1), Now);

        felix.Lift(Moderator, Now.AddMinutes(1));

        Assert.That(felix.BanAt(Now.AddMinutes(1)), Is.Null);
        Assert.That(felix.MuteAt(Now.AddMinutes(1)), Is.Null);
        Assert.That(felix.Entries.Select(e => e.Kind).TakeLast(2),
            Is.EqualTo(new[] { ModerationKind.BanLifted, ModerationKind.MuteLifted }));
    }

    [Test]
    public void Lift_OnlyWhatIsInForce()
    {
        var felix = PlayerModeration.For(Felix);
        felix.MuteUntil(Moderator, "Spam", Now.AddHours(1), Now);
        felix.BanUntil(Moderator, "Spam", Now.AddDays(7), Now);

        // The mute has ended by itself by now: only the ban is lifted.
        felix.Lift(Moderator, Now.AddHours(2));

        Assert.That(felix.Entries.Last().Kind, Is.EqualTo(ModerationKind.BanLifted));
        Assert.That(felix.Entries.Count(e => e.Kind == ModerationKind.MuteLifted), Is.Zero);
    }

    [Test]
    public void Lift_WithNothingInForce_IsRefused()
    {
        var felix = PlayerModeration.For(Felix);
        felix.MuteUntil(Moderator, "Spam", Now.AddHours(1), Now);

        var refused = Assert.Throws<DomainException>(() => felix.Lift(Moderator, Now.AddHours(1)));
        Assert.That(refused!.Message, Is.EqualTo("That cat isn't banned or muted."));
    }

    [Test]
    public void AMutedPlayer_MayNotChat_UntilTheMuteEnds()
    {
        var felix = PlayerModeration.For(Felix);
        felix.EnsureMayChat(Now);
        felix.MuteUntil(Moderator, "Rude in chat", Now.AddHours(1), Now);

        var refused = Assert.Throws<DomainException>(() => felix.EnsureMayChat(Now.AddMinutes(59)));
        Assert.That(refused!.Message, Is.EqualTo("You're muted until 18 Nov 2026 13:00 UTC. Why: Rude in chat"));
        felix.EnsureMayChat(Now.AddHours(1));
        PlayerModeration.EnsureMayChat(mute: null, Now);
    }

    [Test]
    public void Warn_IsShownOnce()
    {
        var felix = PlayerModeration.For(Felix);
        felix.Warn(Moderator, "Please keep it friendly.", Now);

        Assert.That(felix.TakeWarning(), Is.EqualTo("Please keep it friendly."));
        Assert.That(felix.TakeWarning(), Is.Null);
        Assert.That(felix.BanAt(Now), Is.Null, "a warning is only words");
        Assert.That(felix.MuteAt(Now), Is.Null);
    }

    [Test]
    public void EveryAction_IsInTheAuditTrail_WhoWhatWhenWhy()
    {
        var felix = PlayerModeration.For(Felix);
        var other = Moderator.Of(OtherAdmin, Admins);

        felix.Warn(Moderator, "Be nice", Now);
        felix.MuteUntil(other, "Not nice", Now.AddHours(1), Now.AddMinutes(1));
        felix.BanUntil(Moderator, "Still not nice", null, Now.AddMinutes(2));
        felix.Lift(other, Now.AddMinutes(3));

        Assert.That(felix.Entries.Select(e => (e.PlayerId, e.Kind, e.ByPlayerId, e.At, e.Reason, e.Until)), Is.EqualTo(new[]
        {
            (Felix, ModerationKind.Warned, Admin, Now, (string?)"Be nice", (DateTimeOffset?)null),
            (Felix, ModerationKind.Muted, OtherAdmin, Now.AddMinutes(1), "Not nice", Now.AddHours(1)),
            (Felix, ModerationKind.Banned, Admin, Now.AddMinutes(2), "Still not nice", null),
            (Felix, ModerationKind.BanLifted, OtherAdmin, Now.AddMinutes(3), null, null),
            (Felix, ModerationKind.MuteLifted, OtherAdmin, Now.AddMinutes(3), null, null),
        }));
        Assert.That(felix.Entries.Select(e => e.Id).Distinct().Count(), Is.EqualTo(5));
    }

    [Test]
    public void EveryChange_CountsAVersion()
    {
        var felix = PlayerModeration.For(Felix);

        felix.Warn(Moderator, "Be nice", Now);
        felix.TakeWarning();
        felix.TakeWarning();

        Assert.That(felix.Version, Is.EqualTo(2), "a warning taken when there's none changes nothing");
    }

    [Test]
    public void Sanction_SaysItsEnd_InUtc()
    {
        var sanction = new Sanction("Spam", Now, new DateTimeOffset(2026, 11, 19, 9, 5, 0, TimeSpan.FromHours(2)), Admin);

        Assert.That(sanction.Ends(), Is.EqualTo("until 19 Nov 2026 07:05 UTC"));
    }
}

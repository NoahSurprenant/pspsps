using Pspsps.Domain.Moderation;

namespace Pspsps.Domain.Test;

public sealed class AddressBanTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Admin = Guid.NewGuid();
    private static readonly Guid OtherAdmin = Guid.NewGuid();
    private static readonly Guid Felix = Guid.NewGuid();
    private static readonly IReadOnlySet<Guid> Admins = new HashSet<Guid> { Admin, OtherAdmin };
    private static readonly string Network = new('a', AddressHashes.Length);

    private static Moderator Moderator => Moderator.Of(Admin, Admins);

    private static PlayerModeration Banned(Guid playerId, TimeSpan? length = null)
    {
        var player = PlayerModeration.For(playerId);
        player.BanFor(Moderator, "Spamming the square", length, Now);
        return player;
    }

    private static PlayerAddress SeenOn(Guid playerId, string network = "", DateTimeOffset? at = null) =>
        PlayerAddress.Seen(playerId, network.Length > 0 ? network : Network, at ?? Now.AddHours(-1));

    [Test]
    public void ABannedCatsNetwork_IsBannedWithIt_ForADayOrAWeek_WithItsReason()
    {
        var ban = AddressBan.For(Network);

        ban.BanWith(Moderator, SeenOn(Felix), Banned(Felix), TimeSpan.FromDays(7), Now);

        Assert.Multiple(() =>
        {
            Assert.That(ban.BanAt(Now), Is.EqualTo(new Sanction("Spamming the square", Now, Now.AddDays(7), Admin)));
            Assert.That(ban.BanAt(Now.AddDays(7).AddTicks(-1)), Is.Not.Null);
            Assert.That(ban.BanAt(Now.AddDays(7)), Is.Null, "it ends at its time");
            Assert.That(ban.PlayerId, Is.EqualTo(Felix));
            Assert.That(ban.Label, Is.EqualTo("aaaaaaaa"));
            Assert.That(ban.BanAt(Now)!.SaysBanned(), Is.EqualTo(
                "Your cat is banned until 26 Nov 2026 12:00 UTC. Why: Spamming the square"), "what a banned cat is told");
        });
    }

    [Test]
    public void ANetworksBan_IsNeverForGood_NorLongerThanTheCats()
    {
        Assert.That(AddressBan.Lengths, Is.EqualTo(new[] { TimeSpan.FromHours(24), TimeSpan.FromDays(7) }));
        foreach (var length in new[] { TimeSpan.FromHours(1), TimeSpan.FromDays(30), TimeSpan.MaxValue, TimeSpan.Zero })
        {
            var refused = Assert.Throws<DomainException>(
                () => AddressBan.For(Network).BanWith(Moderator, SeenOn(Felix), Banned(Felix), length, Now));
            Assert.That(refused!.Message, Is.EqualTo("A network's ban lasts a day or a week."));
        }

        var shorter = AddressBan.For(Network);
        shorter.BanWith(Moderator, SeenOn(Felix), Banned(Felix, TimeSpan.FromHours(24)), TimeSpan.FromDays(7), Now);
        Assert.That(shorter.BanAt(Now)?.Until, Is.EqualTo(Now.AddHours(24)), "ends with the cat's");
    }

    [Test]
    public void ANetwork_IsBannedOnlyWithItsCatBanned()
    {
        var notBanned = Assert.Throws<DomainException>(() =>
            AddressBan.For(Network).BanWith(Moderator, SeenOn(Felix), PlayerModeration.For(Felix), TimeSpan.FromHours(24), Now));
        Assert.That(notBanned!.Message, Does.StartWith("Ban the cat first"));

        var over = Banned(Felix, TimeSpan.FromHours(24));
        Assert.Throws<DomainException>(() =>
            AddressBan.For(Network).BanWith(Moderator, SeenOn(Felix, at: Now.AddHours(23)), over, TimeSpan.FromHours(24), Now.AddHours(24)));
    }

    [Test]
    public void OnlyANetworkTheCatWasOnLately_IsBanned()
    {
        var felix = Banned(Felix);
        var stale = SeenOn(Felix, at: Now - PlayerAddress.KeepFor);
        var refused = Assert.Throws<DomainException>(
            () => AddressBan.For(Network).BanWith(Moderator, stale, felix, TimeSpan.FromHours(24), Now));
        Assert.That(refused!.Message, Does.Contain("in the last 7 days"));

        Assert.Throws<DomainException>(() => AddressBan.For(Network)
            .BanWith(Moderator, SeenOn(Felix, new string('b', AddressHashes.Length)), felix, TimeSpan.FromHours(24), Now),
            "another network");
        Assert.Throws<DomainException>(() => AddressBan.For(Network)
            .BanWith(Moderator, SeenOn(Guid.NewGuid()), felix, TimeSpan.FromHours(24), Now), "another cat's");
    }

    [Test]
    public void OnlyAnAdmin_BansANetwork_NeverTheirOwnNorAnotherAdmins()
    {
        Assert.Throws<DomainException>(() => Moderator.Of(Felix, Admins), "a moderator is made only from the admin list");

        var self = Assert.Throws<DomainException>(() =>
            AddressBan.For(Network).BanWith(Moderator, SeenOn(Admin), PlayerModeration.For(Admin), TimeSpan.FromHours(24), Now));
        Assert.That(self!.Message, Does.Contain("yourself"));
        var admin = Assert.Throws<DomainException>(() =>
            AddressBan.For(Network).BanWith(Moderator, SeenOn(OtherAdmin), PlayerModeration.For(OtherAdmin), TimeSpan.FromHours(24), Now));
        Assert.That(admin!.Message, Does.Contain("admin"));
    }

    [Test]
    public void AReason_IsAlwaysSaid()
    {
        Assert.Throws<DomainException>(() => PlayerModeration.ReasonOf("   "));
        var ban = AddressBan.For(Network);
        ban.BanWith(Moderator, SeenOn(Felix), Banned(Felix), TimeSpan.FromHours(24), Now);
        Assert.That(ban.Reason, Is.EqualTo("Spamming the square"));
    }

    [Test]
    public void Lift_EndsTheBan_AndIsRefusedWithNoneInForce()
    {
        var ban = AddressBan.For(Network);
        var refused = Assert.Throws<DomainException>(() => ban.Lift(Moderator, Now));
        Assert.That(refused!.Message, Is.EqualTo("That network isn't banned."));
        ban.BanWith(Moderator, SeenOn(Felix), Banned(Felix), TimeSpan.FromHours(24), Now);

        ban.Lift(Moderator.Of(OtherAdmin, Admins), Now.AddHours(1));

        Assert.That(ban.BanAt(Now.AddHours(1)), Is.Null);
        Assert.That(ban.LiftedBy, Is.EqualTo(OtherAdmin));
        Assert.Throws<DomainException>(() => ban.Lift(Moderator, Now.AddHours(2)), "once");

        ban.BanWith(Moderator, SeenOn(Felix, at: Now.AddHours(2)), Banned(Felix), TimeSpan.FromHours(24), Now.AddHours(3));
        Assert.That(ban.BanAt(Now.AddHours(3)), Is.Not.Null, "a new ban takes the place of the last");
    }

    [Test]
    public void EachBanAndLift_IsALineOfTheCatsAuditTrail_WithTheNetworksLabelOnly()
    {
        var ban = AddressBan.For(Network);
        ban.BanWith(Moderator, SeenOn(Felix), Banned(Felix), TimeSpan.FromHours(24), Now);
        ban.Lift(Moderator, Now.AddMinutes(5));

        var lines = ban.Entries.Select(e => (e.PlayerId, e.Kind, e.ByPlayerId, e.At, e.Reason, e.Until, e.Address)).ToList();

        Assert.That(lines, Is.EqualTo(new[]
        {
            (Felix, ModerationKind.AddressBanned, Admin, Now, (string?)"Spamming the square", (DateTimeOffset?)Now.AddHours(24), (string?)"aaaaaaaa"),
            (Felix, ModerationKind.AddressBanLifted, Admin, Now.AddMinutes(5), null, null, "aaaaaaaa"),
        }));
        Assert.That(ban.Version, Is.EqualTo(2));
    }

    [Test]
    public void ANetwork_IsAKeyedHash_NeverAnAddress()
    {
        Assert.Throws<DomainException>(() => AddressBan.For("192.0.2.1"));
        Assert.Throws<DomainException>(() => AddressBan.For(new string('A', AddressHashes.Length)), "lowercase hex only");
        Assert.Throws<DomainException>(() => PlayerAddress.Seen(Felix, "2001:db8::1", Now));
        Assert.That(AddressHashes.IsValid(Network), Is.True);
    }

    [Test]
    public void TheNetworksPicked_AreOneToTwentyKeyedHashes_EachOnce()
    {
        var other = new string('b', AddressHashes.Length);

        Assert.That(AddressBan.Picked([Network, other, Network]), Is.EqualTo(new[] { Network, other }));
        var none = Assert.Throws<DomainException>(() => AddressBan.Picked([]));
        Assert.That(none!.Message, Is.EqualTo("Pick a network to ban."));
        var many = Enumerable.Range(0, AddressBan.MaxAtOnce + 1).Select(i => i.ToString("x64")).ToList();
        Assert.Throws<DomainException>(() => AddressBan.Picked(many));
        Assert.That(AddressBan.Picked(many.Take(AddressBan.MaxAtOnce)), Has.Count.EqualTo(AddressBan.MaxAtOnce));
        Assert.Throws<DomainException>(() => AddressBan.Picked(["192.0.2.1"]));
    }

    [Test]
    public void ANetworksBan_FollowsItsCatsBan_WhenTheCatIsBannedAgainForLess()
    {
        var ban = AddressBan.For(Network);
        ban.BanWith(Moderator, SeenOn(Felix), Banned(Felix), TimeSpan.FromDays(7), Now);
        var felix = Banned(Felix);
        var later = Now.AddHours(1);
        felix.BanFor(Moderator, "Second thoughts", TimeSpan.FromHours(24), later);

        ban.FollowCat(Moderator, felix, later);

        Assert.That(ban.BanAt(later)?.Until, Is.EqualTo(later.AddHours(24)), "never past the cat's ban");
        Assert.That(ban.Entries.Last().Kind, Is.EqualTo(ModerationKind.AddressBanned), "in the audit trail");
        Assert.That(ban.Entries.Last().Until, Is.EqualTo(later.AddHours(24)));

        var count = ban.Entries.Count;
        felix.BanFor(Moderator, "Longer after all", length: null, later);
        ban.FollowCat(Moderator, felix, later);
        Assert.That(ban.BanAt(later)?.Until, Is.EqualTo(later.AddHours(24)), "a longer ban doesn't lengthen it");
        Assert.That(ban.Entries, Has.Count.EqualTo(count), "nothing changed: nothing recorded");
    }

    [Test]
    public void ANetworksBan_IsWithTheCatItWasBannedWith_Only()
    {
        var ban = AddressBan.For(Network);
        ban.BanWith(Moderator, SeenOn(Felix), Banned(Felix), TimeSpan.FromDays(7), Now);

        Assert.That(ban.IsBannedWith(Felix, Now), Is.True);
        Assert.That(ban.IsBannedWith(Guid.NewGuid(), Now), Is.False, "another cat on the network");
        Assert.That(ban.IsBannedWith(Felix, Now.AddDays(7)), Is.False, "over");
    }

    [Test]
    public void APlayersNetwork_IsRemembered7DaysAfterItWasLastSeen_AndSavedAtMostHourly()
    {
        var seen = PlayerAddress.Seen(Felix, Network, Now);

        Assert.That(seen.IsRecentAt(Now.AddDays(7).AddTicks(-1)), Is.True);
        Assert.That(seen.IsRecentAt(Now.AddDays(7)), Is.False);
        Assert.That(seen.SeenAgain(Now.AddMinutes(59)), Is.False, "nothing to save within the hour");
        Assert.That(seen.SeenAgain(Now.AddHours(1)), Is.True);
        Assert.That(seen.IsRecentAt(Now.AddDays(7)), Is.True, "seen again: kept from then");
        Assert.Throws<DomainException>(() => PlayerAddress.Seen(Guid.Empty, Network, Now));
    }
}

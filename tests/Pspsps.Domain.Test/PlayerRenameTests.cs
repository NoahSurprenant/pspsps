using Pspsps.Domain.Players;
using Pspsps.Domain.Rooms;

namespace Pspsps.Domain.Test;

public sealed class PlayerRenameTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    private static readonly ChatFilter Filter = ChatFilter.Create([new ChatWordList(ChatFilterAction.Refuse, ["heck"])]);

    [Test]
    public void Rename_AppliesTheLengthRule()
    {
        var player = Player.CreateGuest("Whiskers", FurColor.Grey, Now);

        Assert.Throws<DomainException>(() => player.Rename("ab", ChatFilter.None, Now));
        Assert.Throws<DomainException>(() => player.Rename(new string('a', 21), ChatFilter.None, Now));
        Assert.That(player.DisplayName, Is.EqualTo("Whiskers"));
    }

    [Test]
    public void Rename_RefusesAFilteredWord()
    {
        var player = Player.CreateGuest("Whiskers", FurColor.Grey, Now);

        var refused = Assert.Throws<DomainException>(() => player.Rename("Heck Cat", Filter, Now));

        Assert.That(refused?.Message, Is.EqualTo(DisplayNameRules.FilteredMessage));
        Assert.That(player.DisplayName, Is.EqualTo("Whiskers"));
    }

    [Test]
    public void Rename_RefusesStaffImpersonation()
    {
        var player = Player.CreateGuest("Whiskers", FurColor.Grey, Now);

        var refused = Assert.Throws<DomainException>(() => player.Rename("Admin Joe", Filter, Now));

        Assert.That(refused?.Message, Is.EqualTo(DisplayNameRules.ImpersonatingMessage));
    }

    [Test]
    public void Rename_SucceedsAndStampsRenamedAt()
    {
        var player = Player.CreateGuest("Whiskers", FurColor.Grey, Now);

        player.Rename("Sir Fluff", Filter, Now);

        Assert.Multiple(() =>
        {
            Assert.That(player.DisplayName, Is.EqualTo("Sir Fluff"));
            Assert.That(player.RenamedAt, Is.EqualTo(Now));
        });
    }

    [Test]
    public void Rename_RefusesASecondRenameWithinTheCooldown()
    {
        var player = Player.CreateGuest("Whiskers", FurColor.Grey, Now);
        player.Rename("Sir Fluff", Filter, Now);

        var tooSoon = Now + Player.RenameCooldown - TimeSpan.FromMinutes(1);
        var refused = Assert.Throws<DomainException>(() => player.Rename("Lady Fluff", Filter, tooSoon));

        Assert.That(refused?.Message, Does.Contain("once every"));
        Assert.That(player.DisplayName, Is.EqualTo("Sir Fluff"));
    }

    [Test]
    public void Rename_AllowsAnotherRenameOnceTheCooldownHasPassed()
    {
        var player = Player.CreateGuest("Whiskers", FurColor.Grey, Now);
        player.Rename("Sir Fluff", Filter, Now);

        var later = Now + Player.RenameCooldown;
        player.Rename("Lady Fluff", Filter, later);

        Assert.Multiple(() =>
        {
            Assert.That(player.DisplayName, Is.EqualTo("Lady Fluff"));
            Assert.That(player.RenamedAt, Is.EqualTo(later));
        });
    }

    [Test]
    public void RenameByAdmin_SkipsTheCooldown()
    {
        var player = Player.CreateGuest("Whiskers", FurColor.Grey, Now);
        player.Rename("Sir Fluff", Filter, Now);

        // Moments later, still well within the player's own cooldown: an admin fixing a rude name isn't held to it.
        player.RenameByAdmin("Nicer Name", Filter);

        Assert.That(player.DisplayName, Is.EqualTo("Nicer Name"));
    }

    [Test]
    public void RenameByAdmin_DoesNotStartOrResetThePlayersOwnCooldown()
    {
        var player = Player.CreateGuest("Whiskers", FurColor.Grey, Now);

        player.RenameByAdmin("Nicer Name", Filter);

        Assert.That(player.RenamedAt, Is.Null);
        // The player can still rename themself right away: unaffected by the admin's rename.
        Assert.DoesNotThrow(() => player.Rename("Their Own Choice", Filter, Now));
    }

    [Test]
    public void RenameByAdmin_StillAppliesTheNameRules()
    {
        var player = Player.CreateGuest("Whiskers", FurColor.Grey, Now);

        Assert.Throws<DomainException>(() => player.RenameByAdmin("Heck Cat", Filter));
        Assert.Throws<DomainException>(() => player.RenameByAdmin("Admin Joe", Filter));
        Assert.Throws<DomainException>(() => player.RenameByAdmin("ab", Filter));
    }
}

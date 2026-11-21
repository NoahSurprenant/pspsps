using Pspsps.Domain;
using Pspsps.Domain.Rooms;

namespace Pspsps.Domain.Test;

public sealed class RoomLookTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    private static readonly RoomLook Good = new("#bfe3f2", "#9fd38a", "#cfc2a8", new LookBackdrop([]))
    {
        Floor = new LookPaint("planks", "#aa8855"),
        Wall = new LookPaint("bricks", Accent: "#123456"),
        Ground = new LookGround("#d9ccb2", "street"),
        Wildlife = "butterfly",
    };

    [Test]
    public void AGoodLook_HasNoProblems() => Assert.That(Good.Problems("loft"), Is.Empty);

    [Test]
    public void ALook_NeedsHexColoursAndPaintersTheBrowserKnows()
    {
        var bad = Good with
        {
            Sky = "blue",
            Floor = new LookPaint("marble"),
            Wall = new LookPaint("bricks", "red"),
            Ground = new LookGround("#d9ccb2", "mud"),
            Wildlife = "dragon",
            Backdrop = new LookBackdrop([], "castle"),
        };

        var problems = bad.Problems("loft");

        Assert.Multiple(() =>
        {
            Assert.That(problems, Has.Some.Contains("its sky"));
            Assert.That(problems, Has.Some.Contains("'marble' is not a floor painter"));
            Assert.That(problems, Has.Some.Contains("the wall painter's colours"));
            Assert.That(problems, Has.Some.Contains("'mud' is not a ground painter"));
            Assert.That(problems, Has.Some.Contains("'dragon' is not wildlife"));
            Assert.That(problems, Has.Some.Contains("'castle' is not a backdrop generator"));
        });
    }

    [Test]
    public void ABackdropsParts_AreHeldToTheSameRulesAsAPropsParts()
    {
        var bad = Good with { Backdrop = new LookBackdrop([new PropPart("box", [0, 1, 2], "nope")]) };

        Assert.That(bad.Problems("loft"), Has.Some.Contains("backdrop part 1"));
    }

    [Test]
    public void AKeptLook_IsCheckedWhenItIsMadeAndWhenItIsReplaced()
    {
        var entry = RoomLookEntry.Of("loft", Good, Now);

        Assert.Multiple(() =>
        {
            Assert.That(entry.Look, Is.EqualTo(Good));
            Assert.That(() => entry.Replace(Good with { WallColour = "x" }, Now), Throws.TypeOf<DomainException>());
            Assert.That(entry.Look, Is.EqualTo(Good), "a refused look changes nothing");
            Assert.That(() => RoomLookEntry.Of("loft", Good with { Sky = "x" }, Now), Throws.TypeOf<DomainException>());
        });
    }

    [Test]
    public void AVariantFloor_NamesAFloorPainterTheEntryKnows_LikeALooksFloor()
    {
        var floor = new LookPaint("rink-ice", "#d9f0fa");
        var known = LookNames.BuiltIn.With([(PaintSurfaces.Floor, "rink-ice")], []);

        Assert.Multiple(() =>
        {
            Assert.That(() => LookVariantEntry.Of("frozen", new LookVariant("#112233", "#445566", floor), Now), Throws.TypeOf<DomainException>(), "unknown to the built-in names");
            Assert.That(() => LookVariantEntry.Of("frozen", new LookVariant("#112233", "#445566", new LookPaint("brick")), Now, known), Throws.TypeOf<DomainException>());
            Assert.That(LookVariantEntry.Of("frozen", new LookVariant("#112233", "#445566", floor), Now, known).ToVariant().Floor, Is.EqualTo(floor));
            Assert.That(LookVariantEntry.Of("frozen", new LookVariant("#112233", "#445566", new LookPaint("tiles")), Now).Floor?.Painter, Is.EqualTo("tiles"));
        });
    }

    [Test]
    public void AVariant_NeedsANameAndTwoColours()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new LookVariant("#112233", "#445566").Problems("spring"), Is.Empty);
            Assert.That(new LookVariant("#112233", "#445566").Problems("Bad Name"), Has.Count.EqualTo(1));
            Assert.That(new LookVariant("#112", "#445566").Problems("spring"), Has.Count.EqualTo(1));
            Assert.That(() => LookVariantEntry.Of("spring", new LookVariant("#112233", "nope"), Now), Throws.TypeOf<DomainException>());
            Assert.That(LookVariantEntry.Of("spring", new LookVariant("#112233", "#445566"), Now).ToVariant(), Is.EqualTo(new LookVariant("#112233", "#445566")));
        });
    }
}

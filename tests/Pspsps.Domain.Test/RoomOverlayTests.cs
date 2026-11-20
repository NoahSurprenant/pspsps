using Pspsps.Domain.Rooms;

namespace Pspsps.Domain.Test;

public sealed class RoomOverlayTests
{
    private static DateTimeOffset On(int year, int month, int day) => new(year, month, day, 12, 0, 0, TimeSpan.Zero);

    private static readonly OverlayWindow Winter = new(On(2026, 12, 1), On(2027, 1, 6), Yearly: true);

    private static readonly RoomDefinition Square = RoomCatalog.Find("town-square")!;

    private static RoomOverlay Overlay(string id = "winter", OverlayWindow? window = null) =>
        new(id, Square.Id, "Winter", window ?? Winter);

    [TestCase(2026, 12, 1, true, TestName = "AYearlyWindow_IsOnFromItsFirstDay")]
    [TestCase(2026, 12, 25, true)]
    [TestCase(2030, 1, 2, true, TestName = "AYearlyWindow_RepeatsInOtherYearsAndRunsOverNewYear")]
    [TestCase(2030, 1, 6, false, TestName = "AYearlyWindow_IsOffOnItsEndDay")]
    [TestCase(2026, 3, 1, false)]
    [TestCase(2026, 11, 30, false)]
    public void AYearlyWindow(int year, int month, int day, bool on) =>
        Assert.That(Winter.Covers(On(year, month, day)), Is.EqualTo(on));

    [Test]
    public void AOneOffWindow_IsOnlyOnceAndEndsWhereItEnds()
    {
        var window = new OverlayWindow(On(2026, 12, 1), On(2026, 12, 8));
        Assert.Multiple(() =>
        {
            Assert.That(window.Covers(On(2026, 12, 1)), Is.True);
            Assert.That(window.Covers(On(2026, 12, 7)), Is.True);
            Assert.That(window.Covers(On(2026, 12, 8)), Is.False);
            Assert.That(window.Covers(On(2027, 12, 3)), Is.False, "not again next year");
        });
    }

    [Test]
    public void AnOverlayThatIsOn_AddsItsPropsRemovesItsOwnAndPaintsTheRoom()
    {
        var gone = Square.Props.First(prop => prop.Kind == PropKinds.FlowerBed);
        var lamp = new Prop(PropKinds.Lamp, new Position(-10.5, 0), Radius: 0.35);
        var overlay = Overlay() with { Adds = [lamp], Removes = [gone], Look = "winter" };

        var now = RoomOverlays.Apply(Square, [overlay], On(2026, 12, 20));

        Assert.Multiple(() =>
        {
            Assert.That(now.Props, Does.Contain(lamp));
            Assert.That(now.Props, Does.Not.Contain(gone));
            Assert.That(now.Props, Has.Count.EqualTo(Square.Props.Count));
            Assert.That(now.Look, Is.EqualTo("winter"));
            Assert.That(Square.Look, Is.Null, "the room itself is not changed");
        });
    }

    [Test]
    public void AnOverlayThatIsOff_OrAnotherRooms_LeavesTheRoomAsItIs()
    {
        var overlay = Overlay() with { Adds = [new Prop(PropKinds.Lamp, new Position(-10.5, 0), 0.35)], Look = "winter" };
        var other = overlay with { Id = "other", RoomId = "cafe" };

        Assert.Multiple(() =>
        {
            Assert.That(RoomOverlays.Apply(Square, [overlay], On(2026, 7, 1)), Is.SameAs(Square));
            Assert.That(RoomOverlays.Apply(Square, [other], On(2026, 12, 20)), Is.SameAs(Square));
        });
    }

    [Test]
    public void OverlaysThatAreOnTogether_LaterOneWins_TheLook()
    {
        var early = Overlay("a", new OverlayWindow(On(2026, 12, 1), On(2027, 1, 1))) with { Look = "snow" };
        var late = Overlay("b", new OverlayWindow(On(2026, 12, 10), On(2027, 1, 1))) with { Look = "party" };

        Assert.That(RoomOverlays.Apply(Square, [late, early], On(2026, 12, 20)).Look, Is.EqualTo("party"));
    }

    [Test]
    public void AGoodOverlay_HasNoProblems()
    {
        var overlay = Overlay() with { Adds = [new Prop(PropKinds.Lamp, new Position(-10.5, 0), 0.35)], Look = "winter" };

        Assert.That(RoomOverlays.Problems(Square, [overlay]), Is.Empty);
    }

    [Test]
    public void AnOverlay_IsHeldToTheRulesForWhereThingsStand()
    {
        var onTheDoor = Overlay() with { Adds = [new Prop(PropKinds.Tree, Square.Doors[0].Spot, 1)] };

        var problems = RoomOverlays.Problems(Square, [onTheDoor]);

        Assert.That(problems, Has.Some.Matches<string>(p => p.StartsWith("town-square/winter: town-square: the tree at") && p.Contains("is on the door")));
    }

    [Test]
    public void AnOverlay_ThatTakesAwayWhatTheRoomHasNot_IsAProblem()
    {
        var overlay = Overlay() with { Removes = [new Prop(PropKinds.Piano, new Position(1, 1), 1)] };

        Assert.That(RoomOverlays.Problems(Square, [overlay]), Has.Some.Matches<string>(p => p.Contains("takes away a piano")));
    }

    [Test]
    public void TwoOverlaysThatClash_WhenBothAreOn_AreAProblemEvenIfEachIsFine()
    {
        var spot = new Position(-10.5, 0);
        var one = Overlay("one") with { Adds = [new Prop(PropKinds.Lamp, spot, 0.35)] };
        var two = Overlay("two") with { Adds = [new Prop(PropKinds.Lamp, spot, 0.35)] };

        var problems = RoomOverlays.Problems(Square, [one, two]);

        Assert.That(problems, Has.Some.Matches<string>(p => p.StartsWith("town-square, with all its overlays on:") && p.Contains("overlaps")));
        Assert.That(problems.Where(problem => problem.StartsWith("town-square/")), Is.Empty);
    }

    [Test]
    public void AnOverlay_NeedsAnIdAWindowThatMakesSenseAndItsOwnRoom()
    {
        var backwards = Overlay("backwards", new OverlayWindow(On(2026, 12, 8), On(2026, 12, 1)));
        var allYear = Overlay("all-year", new OverlayWindow(On(2026, 3, 1), On(2027, 3, 1), Yearly: true));
        var elsewhere = Overlay("elsewhere") with { RoomId = "cafe" };
        var twice = Overlay("twice");

        var problems = RoomOverlays.Problems(Square, [backwards, allYear, elsewhere, twice, twice]);

        Assert.Multiple(() =>
        {
            Assert.That(problems, Has.Some.EqualTo("town-square/backwards: ends before it starts"));
            Assert.That(problems, Has.Some.EqualTo("town-square/all-year: starts and ends on the same day of the year"));
            Assert.That(problems, Has.Some.Matches<string>(p => p.Contains("is an overlay of cafe")));
            Assert.That(problems, Has.Some.Matches<string>(p => p.Contains("is not the only overlay")));
        });
    }

    [Test]
    public void ABadRoom_IsNotBlamedOnItsOverlays()
    {
        var crowded = Square with { Props = [.. Square.Props, new Prop(PropKinds.Tree, Square.Doors[0].Spot, 1)] };

        Assert.That(RoomOverlays.Problems(crowded, [Overlay() with { Look = "winter" }]), Is.Empty);
    }
}

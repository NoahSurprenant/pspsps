using Pspsps.Domain.Rooms;

namespace Pspsps.Domain.Test;

public sealed class RoomPlanTests
{
    [Test]
    public void AShallowRoomMustHaveSpaceToStepThroughItsDoor()
    {
        var shallow = new RoomDefinition("hall", "Hall", 8, 2.5, new(0, 0), 10);
        Assert.That(() => shallow.WithDoors(("out", RoomEdge.South, 0, "square", "hall")),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [TestCase(RoomEdge.North, 2.5, 1)]
    [TestCase(RoomEdge.South, 2.5, -1)]
    [TestCase(RoomEdge.East, 1, 2.5)]
    [TestCase(RoomEdge.West, -1, 2.5)]
    public void ArrivalFollowsTheCentrelineAcrossTheThreshold(RoomEdge edge, double x, double z)
    {
        var room = new RoomDefinition("hall", "Hall", 7.1, 7.1, new(0, 0), 10)
            .WithDoors(("out", edge, 2.5, "square", "hall"));
        var arrival = room.GetDoor("out").Arrival;
        Assert.That(arrival.X, Is.EqualTo(x).Within(1e-9));
        Assert.That(arrival.Z, Is.EqualTo(z).Within(1e-9));
    }

    [Test]
    public void DoorsNeedNotLineUpOnAnyMap_BecauseTheMapIsNotMadeFromThem()
    {
        var shop = RoomCatalog.Find("shop")!.WithDoors(("town-square", RoomEdge.West, 1.5, "town-square", "shop"));
        Assert.That(() => Replan(shop), Throws.Nothing);
    }

    [Test]
    public void ARoomNobodyCanWalkTo_IsAProblem()
    {
        var island = new RoomDefinition("island", "Island", 10, 10, new(0, 0), 10);
        Assert.That(() => RoomPlan.Join([.. RoomCatalog.All, island]),
            Throws.InvalidOperationException.With.Message.Contains("walking route"));
    }

    [Test]
    public void ARoomPlacedAgainstAnotherOnTheGrid_IsWalkedToWithNoDoor_ButOneAtADistanceIsNot()
    {
        var square = RoomCatalog.Find("town-square")!;
        var anchored = square with { Place = new WorldPlace(0, 0) };
        var next = new RoomDefinition("meadow", "Meadow", 10, 10, new(0, 0), 10) { Place = new WorldPlace((int)(square.Width / WorldPlace.Cell), 0) };
        var far = next with { Place = new WorldPlace(next.Place!.X + 50, 0) };
        IReadOnlyList<RoomDefinition> Plan(RoomDefinition room) =>
            RoomPlan.Join([.. RoomCatalog.All.Select(r => r.Id == square.Id ? anchored : r), room]);

        Assert.Multiple(() =>
        {
            Assert.That(() => Plan(next), Throws.Nothing);
            Assert.That(() => Plan(far), Throws.InvalidOperationException.With.Message.Contains("walking route"));
        });
    }

    private static IReadOnlyList<RoomDefinition> Replan(RoomDefinition replacement) =>
        RoomPlan.Join([.. RoomCatalog.All.Select(room => room.Id == replacement.Id ? replacement : room)]);

    [Test]
    public void ResizingTheFloor_DragsItsDoorWithTheEdge()
    {
        var small = new RoomDefinition("test", "Test Room", Width: 8, Depth: 6, Spawn: new(0, 0), Capacity: 10);
        var large = small with { Depth = 10 };
        var exit = ("garden", RoomEdge.South, 2d, "garden", "test");

        Assert.That(small.WithDoors(exit).GetDoor("garden").Spot, Is.EqualTo(new Position(2, -2)));
        Assert.That(large.WithDoors(exit).GetDoor("garden").Spot, Is.EqualTo(new Position(2, -4)));
        var tooNarrow = small with { Width = 5 };
        Assert.That(() => tooNarrow.WithDoors(exit), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => small.WithDoors(exit, ("other", RoomEdge.South, 2.5d, "other", "test")),
            Throws.TypeOf<ArgumentException>());
    }
}

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

    [TestCase(19, 2.51)] // A one-centimetre lateral error at a threshold.
    [TestCase(19.02, 2.5)] // A one-centimetre gap after walking around the block.
    public void ALoopMustCloseFromBothDirections(double depth, double offset)
    {
        var orchard = RoomCatalog.Find("pear-orchard")! with { Depth = depth };
        orchard = orchard.WithDoors(
            ("garden", RoomEdge.North, 5.8, "garden", "pear-orchard"),
            ("riverside", RoomEdge.East, offset, "riverside", "pear-orchard"));
        Assert.That(() => Replan(orchard), Throws.InvalidOperationException.With.Message.Contains("does not close"));
    }

    [Test]
    public void ABuildingCannotExtendIntoItsNeighbour()
    {
        var cafe = RoomCatalog.Find("cafe")! with { Width = 18 };
        Assert.That(() => Replan(cafe), Throws.InvalidOperationException.With.Message.Contains("overlaps"));
    }

    [Test]
    public void ARotatedInteriorMustFaceItsOutsideDoor()
    {
        var cafe = RoomCatalog.Find("cafe")! with { MapRotated = false };
        Assert.That(() => Replan(cafe), Throws.InvalidOperationException.With.Message.Contains("Mismatched thresholds"));
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

    [Test]
    public void PublicRoomFootprints_DoNotOverlap()
    {
        var rooms = RoomCatalog.All;
        for (var i = 0; i < rooms.Count; i++)
        {
            var a = rooms[i];
            foreach (var b in rooms.Skip(i + 1))
            {
                var overlapX = 1e-6 + Math.Abs(a.MapCentre.X - b.MapCentre.X) < (a.Width + b.Width) / 2;
                var overlapZ = 1e-6 + Math.Abs(a.MapCentre.Z - b.MapCentre.Z) < (a.Depth + b.Depth) / 2;
                Assert.That(overlapX && overlapZ, Is.False, $"{a.Id} overlaps {b.Id} on the map.");
            }
        }
    }

    [Test]
    public void AnExitFacesItsDestination_OnTheWorldMap()
    {
        var rooms = RoomCatalog.All.ToDictionary(room => room.Id);
        foreach (var room in rooms.Values)
        {
            foreach (var door in room.Doors)
            {
                var target = rooms[door.ToRoom];
                var exit = room.MapExit(door);
                var back = target.GetDoor(door.ToDoor!);
                Assert.That(target.MapExit(back).DistanceTo(exit), Is.LessThan(1e-6), $"{room.Id}/{door.Id} leaves a gap.");
                var direction = room.MapRotated ? -1 : 1;
                var clearance = door.Edge switch
                {
                    RoomEdge.North => direction * (target.MapCentre.Z - exit.Z) - target.Depth / 2,
                    RoomEdge.East => direction * (target.MapCentre.X - exit.X) - target.Width / 2,
                    RoomEdge.South => direction * (exit.Z - target.MapCentre.Z) - target.Depth / 2,
                    RoomEdge.West => direction * (exit.X - target.MapCentre.X) - target.Width / 2,
                    _ => throw new InvalidOperationException($"No edge on {room.Id}/{door.Id}."),
                };
                Assert.That(clearance, Is.EqualTo(0).Within(1e-6), $"{room.Id}/{door.Id} must face {target.Id}.");
            }
        }
    }
}

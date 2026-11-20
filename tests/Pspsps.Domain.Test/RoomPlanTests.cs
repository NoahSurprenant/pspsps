using Pspsps.Domain.Rooms;

namespace Pspsps.Domain.Test;

public sealed class RoomPlanTests
{
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
                var overlapX = Math.Abs(a.MapCentre.X - b.MapCentre.X) < (a.Width + b.Width) / 2;
                var overlapZ = Math.Abs(a.MapCentre.Z - b.MapCentre.Z) < (a.Depth + b.Depth) / 2;
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
                var direction = room.MapRotated ? -1 : 1;
                var clearance = door.Edge switch
                {
                    RoomEdge.North => direction * (target.MapCentre.Z - exit.Z) - target.Depth / 2,
                    RoomEdge.East => direction * (target.MapCentre.X - exit.X) - target.Width / 2,
                    RoomEdge.South => direction * (exit.Z - target.MapCentre.Z) - target.Depth / 2,
                    RoomEdge.West => direction * (exit.X - target.MapCentre.X) - target.Width / 2,
                    _ => throw new InvalidOperationException($"No edge on {room.Id}/{door.Id}."),
                };
                Assert.That(clearance, Is.GreaterThan(0), $"{room.Id}/{door.Id} must face {target.Id}.");
            }
        }
    }
}

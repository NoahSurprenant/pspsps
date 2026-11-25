using Pspsps.Domain;
using Pspsps.Domain.Rooms;
using Pspsps.Server.Game;

namespace Pspsps.Server.Test.Game;

public sealed class ChunkViewsTests
{
    [Test]
    public void ARoomsPath_IsSentAsAStripInMetresFromItsMiddle_AndARoomWithNoneSendsNone()
    {
        var west = new RoomDefinition("west", "West", 20, 20, new Position(0, 0), 10)
        {
            Place = new WorldPlace(0, 0),
            Paths = [new RoomPath(RoomEdge.North, 4, 2, 10), new RoomPath(RoomEdge.West, -2, 2, 6)],
        };
        var east = new RoomDefinition("east", "East", 20, 20, new Position(0, 0), 10) { Place = new WorldPlace(10, 0) };

        var chunks = ChunkViews.Of([west, east]);

        Assert.Multiple(() =>
        {
            Assert.That(chunks.Single(chunk => chunk.RoomId == "west").Paths, Is.EqualTo(new[] { new PathView(4, 15, 2, 10), new PathView(-13, -2, 6, 2) }));
            Assert.That(chunks.Single(chunk => chunk.RoomId == "east").Paths, Is.Null);
        });
    }

    [Test]
    public void EveryPlacedRoom_IsDrawnAtItsWorldSpot_WithItsDoorsNamed_AndARoomOffTheMapIsLeftOut()
    {
        var west = new RoomDefinition("west", "West", 20, 20, new Position(0, 0), 10) { Place = new WorldPlace(0, 0) }
            .WithDoors(("in", RoomEdge.South, 0, "hall", "out"));
        var east = new RoomDefinition("east", "East", 20, 20, new Position(0, 0), 10) { Place = new WorldPlace(10, 0) };
        var hall = new RoomDefinition("hall", "The Hall", 8, 8, new Position(0, 0), 5);

        var chunks = ChunkViews.Of([west, east, hall]);

        Assert.Multiple(() =>
        {
            Assert.That(chunks.Select(chunk => chunk.RoomId), Is.EquivalentTo(new[] { "west", "east" }));
            var drawn = chunks.Single(chunk => chunk.RoomId == "east");
            Assert.That((drawn.X, drawn.Z), Is.EqualTo((30d, 10d)), "its middle, in world metres");
            Assert.That(chunks.Single(chunk => chunk.RoomId == "west").Doors.Single().ToRoomName, Is.EqualTo("The Hall"));
        });
    }
}

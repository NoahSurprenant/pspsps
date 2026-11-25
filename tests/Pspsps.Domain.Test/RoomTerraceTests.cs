using Pspsps.Domain.Rooms;

namespace Pspsps.Domain.Test;

public sealed class RoomTerraceTests
{
    private static RoomDefinition Room(params RoomTerrace[] terraces) =>
        new("r", "R", 20, 20, new Position(0, 0), 10) { Place = new WorldPlace(0, 0), Terraces = terraces };

    [Test]
    public void ATerraceInsideTheFloor_IsFine_AndSurvivesTheLayoutRoundTrip()
    {
        var room = Room(new RoomTerrace(1, -2, 8, 6, 0.5));

        Assert.Multiple(() =>
        {
            Assert.That(RoomTerrace.Problems(room), Is.Empty);
            Assert.That(RoomLayout.Of(room).ToDefinition("r").Terraces, Is.EqualTo(room.Terraces));
            Assert.That(room.SameAs(room with { Terraces = [] }), Is.False, "a raised room is not the flat one");
        });
    }

    [TestCase(1, 0, 1, 6, 0.5, "is under 2 m across")]
    [TestCase(0, 0, 8, 6, 0, "is not between 0 and 3 m high")]
    [TestCase(0, 0, 8, 6, 3.5, "is not between 0 and 3 m high")]
    [TestCase(8, 0, 8, 6, 0.5, "reaches past the room's floor")]
    [TestCase(double.NaN, 0, 8, 6, 0.5, "has no size")]
    public void ABadTerrace_IsNamedWithItsProblem(double x, double z, double width, double depth, double height, string problem) =>
        Assert.That(RoomTerrace.Problems(Room(new RoomTerrace(x, z, width, depth, height))), Is.EqualTo(new[] { $"r/terrace 1: {problem}." }));

    [Test]
    public void ARoomOffTheWorldMap_CannotHaveATerrace() =>
        Assert.That(RoomTerrace.Problems(Room(new RoomTerrace(0, 0, 8, 6, 0.5)) with { Place = null }), Is.EqualTo(new[] { "r/terrace 1: is in a room with no place on the world map." }));

    [Test]
    public void ALayoutWithNullTerraces_HasNone() =>
        Assert.That(RoomLayout.Of(Room()) with { Terraces = null! }, Has.Property(nameof(RoomLayout.Terraces)).Empty);
}

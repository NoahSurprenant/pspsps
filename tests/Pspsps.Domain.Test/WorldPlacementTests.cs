using Pspsps.Domain.Rooms;

namespace Pspsps.Domain.Test;

public sealed class WorldPlacementTests
{
    private static Dictionary<string, RoomLayout> Seed() =>
        RoomCatalog.All.ToDictionary(room => room.Id, RoomLayout.Of);

    private static int Cells(double metres) => (int)Math.Round(metres / WorldPlace.Cell);

    [Test]
    public void RoomsPlacedSideBySide_TouchingEdges_AreFine()
    {
        var layouts = Seed();
        var square = layouts[RoomBook.StartingRoom] with { Place = new WorldPlace(0, 0) };
        layouts[RoomBook.StartingRoom] = square;
        layouts["cafe"] = layouts["cafe"] with { Place = new WorldPlace(Cells(square.Width), 0) };

        Assert.That(RoomBook.Problems(layouts, []), Is.Empty);
    }

    [Test]
    public void RoomsPlacedOnTopOfEachOther_AreAProblem_NamedInOrder()
    {
        var layouts = Seed();
        layouts[RoomBook.StartingRoom] = layouts[RoomBook.StartingRoom] with { Place = new WorldPlace(0, 0) };
        layouts["cafe"] = layouts["cafe"] with { Place = new WorldPlace(1, 1) };

        Assert.That(RoomBook.Problems(layouts, []), Is.EqualTo(new[] { "cafe and town-square: overlap on the world map." }));
    }

    [Test]
    public void APlacedRoom_WhoseFloorIsNotWholeCells_IsAProblem()
    {
        var layouts = Seed();
        layouts["cafe"] = layouts["cafe"] with { Width = layouts["cafe"].Width + 0.5, Place = new WorldPlace(100, 100) };

        Assert.That(RoomBook.Problems(layouts, []), Has.Some.Matches<string>(p => p.StartsWith("cafe: a placed room's floor is whole", StringComparison.Ordinal)));
    }

    [Test]
    public void AnUnplacedRoom_MayBeAnySize_AndNeverOverlaps()
    {
        var layouts = Seed();
        layouts["cafe"] = layouts["cafe"] with { Width = layouts["cafe"].Width + 0.5 };

        Assert.That(RoomBook.Problems(layouts, []), Is.Empty);
    }
}

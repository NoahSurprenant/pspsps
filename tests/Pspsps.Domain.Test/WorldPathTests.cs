using Pspsps.Domain.Rooms;

namespace Pspsps.Domain.Test;

public sealed class WorldPathTests
{
    // "a" and "b" are 20 m squares with 10 m between them (a: z 0..20, b: z 30..50); the path leaves a's north edge at its middle (x 9..11).
    private static RoomDefinition A(params RoomPath[] paths) =>
        new("a", "A", 20, 20, new Position(0, 0), 10) { Place = new WorldPlace(0, 0), Paths = paths.Length == 0 ? [new RoomPath(RoomEdge.North, 0, 2, 10)] : paths };

    private static RoomDefinition B() => new("b", "B", 20, 20, new Position(0, 0), 10) { Place = new WorldPlace(0, 15) };

    private static Dictionary<string, RoomLayout> Seed() => RoomCatalog.All.ToDictionary(room => room.Id, RoomLayout.Of);

    // The café 10 m north of the square, its middle over the square's, with a path from the square's north edge, as the given path says.
    private static Dictionary<string, RoomLayout> SquareAndAFarCafe(RoomPath path, int cafeCellsAbove = 5)
    {
        var layouts = Seed();
        var square = layouts[RoomBook.StartingRoom] with { Place = new WorldPlace(0, 0), Paths = [path] };
        var cafe = layouts["cafe"];
        layouts[RoomBook.StartingRoom] = square;
        layouts["cafe"] = cafe with
        {
            Place = new WorldPlace((int)Math.Floor(((square.Width / 2) - (cafe.Width / 2)) / WorldPlace.Cell), (int)Math.Round(square.Depth / WorldPlace.Cell) + cafeCellsAbove),
        };
        return layouts;
    }

    [Test]
    public void APath_IsFloorBetweenTwoRoomsThatDoNotTouch()
    {
        var space = new WorldSpace([A(), B()]);

        Assert.Multiple(() =>
        {
            Assert.That(space.Contains(new Position(10, 25)), Is.True, "on the path");
            Assert.That(space.Contains(new Position(5, 25)), Is.False, "beside it");
            Assert.That(space.RoomAt(new Position(10, 25))!.Id, Is.EqualTo("a"), "a path belongs to the room it leaves");
            Assert.That(space.Clamp(new Position(5, 25)), Is.EqualTo(new Position(9, 25)), "a cat is held to the path");
            Assert.That(space.Clamp(new Position(10, 29.9)), Is.EqualTo(new Position(10, 29.9)));
        });
    }

    [Test]
    public void APath_ReachesTheRoomAtItsFarEnd_AndNoOther()
    {
        var a = A();

        Assert.Multiple(() =>
        {
            Assert.That(RoomPaths.Reaches(a, a.Paths[0], B()), Is.True);
            Assert.That(RoomPaths.Reaches(a, a.Paths[0], B() with { Place = new WorldPlace(0, 16) }), Is.False, "two metres short");
            Assert.That(RoomPaths.Reaches(a, a.Paths[0], B() with { Place = new WorldPlace(10, 15) }), Is.False, "not under the whole strip");
            Assert.That(RoomPaths.Problems([a, B()]), Is.Empty);
        });
    }

    [Test]
    public void APathThatEndsNowhere_IsAProblem()
    {
        Assert.That(RoomPaths.Problems([A(new RoomPath(RoomEdge.North, 0, 2, 4)), B()]), Is.EqualTo(new[] { "a/path 1: doesn't end at another room." }));
    }

    [Test]
    public void APathThatRunsOverARoom_IsAProblem()
    {
        var problems = RoomPaths.Problems([A(new RoomPath(RoomEdge.North, 0, 2, 14)), B()]);

        Assert.That(problems, Has.Member("a/path 1: runs over b."));
    }

    [Test]
    public void APathWiderThanTheEdgeItLeaves_IsAProblem()
    {
        Assert.That(RoomPaths.Problems([A(new RoomPath(RoomEdge.North, 9.5, 2, 10)), B()]), Is.EqualTo(new[] { "a/path 1: doesn't fit on the North edge." }));
    }

    [Test]
    public void TwoPathsOnTopOfEachOther_AreAProblem()
    {
        var b = B() with { Paths = [new RoomPath(RoomEdge.South, 0, 2, 10)] };

        Assert.That(RoomPaths.Problems([A(), b]), Has.Member("a/path 1: crosses b/path 1."));
    }

    [Test]
    public void AnIndoorRoom_CannotHavePaths()
    {
        var indoors = new RoomDefinition("in", "In", 8, 8, new Position(0, 0), 5) { Paths = [new RoomPath(RoomEdge.North, 0, 2, 4)] };

        Assert.That(RoomPaths.Problems([indoors]), Is.EqualTo(new[] { "in: has paths but no place on the world map." }));
    }

    [Test]
    public void RoomsLinkedByAPath_AreOneWorld()
    {
        var layouts = SquareAndAFarCafe(new RoomPath(RoomEdge.North, 0, 2, 10));

        Assert.That(RoomBook.Problems(layouts, []), Is.Empty);
    }

    [Test]
    public void ARoomBook_NamesAPathThatMissesItsRoom()
    {
        var layouts = SquareAndAFarCafe(new RoomPath(RoomEdge.North, 0, 2, 8));

        Assert.That(RoomBook.Problems(layouts, []), Is.EqualTo(new[] { $"{RoomBook.StartingRoom}/path 1: doesn't end at another room." }));
    }

    [Test]
    public void ARoomWithAnotherPath_IsNotTheSameRoomToACat()
    {
        Assert.Multiple(() =>
        {
            Assert.That(A().SameAs(A()), Is.True);
            Assert.That(A().SameAs(A(new RoomPath(RoomEdge.North, 0, 4, 10))), Is.False);
        });
    }

    [Test]
    public void ALayout_KeepsItsPaths_ThroughItsDefinition()
    {
        var layout = SquareAndAFarCafe(new RoomPath(RoomEdge.North, 0, 2, 10))[RoomBook.StartingRoom];

        var back = RoomLayout.Of(layout.ToDefinition(RoomBook.StartingRoom));

        Assert.That(back.Paths, Is.EqualTo(layout.Paths));
    }

    [Test]
    public void ALayoutWithNullPaths_HasNone_SoNothingThrowsOnIt()
    {
        var layout = System.Text.Json.JsonSerializer.Deserialize<RoomLayout>(
            """{"name":"A","width":20,"depth":20,"spawn":{"x":0,"z":0},"capacity":10,"paths":null}""", System.Text.Json.JsonSerializerOptions.Web)!;

        Assert.That(layout.Paths, Is.Empty);
        Assert.That(RoomBook.Problems(new Dictionary<string, RoomLayout> { ["a"] = layout }, []), Is.Not.Null);
    }
}

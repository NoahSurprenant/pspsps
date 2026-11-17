using Pspsps.Domain.Rooms;

namespace Pspsps.Domain.Test;

public sealed class PropPlacementTests
{
    private static readonly Door Out = new("out", new Position(6, 4), Radius: 0.8, ToRoom: "elsewhere", ToDoor: "in");

    /// <summary>A 20 by 12 room: spawn at (0, -3), a door at (6, 4) by the back, a pond at (-5, 1).</summary>
    private static RoomDefinition RoomWith(params Prop[] props) =>
        new("test", "Test Room", Width: 20, Depth: 12, Spawn: new(0, -3), Capacity: 10, Pond: new Pond(new(-5, 1), Radius: 1.5))
        {
            Doors = [Out],
            Props = props,
        };

    private static Prop Bench(double x, double z, double radius = 1) => new(PropKinds.Bench, new Position(x, z), radius);

    [Test]
    public void EveryRoomsProps_KeepClearOfTheDoorsThePondTheSpawnPointAndTheWalksBetweenThem()
    {
        foreach (var room in RoomCatalog.All)
        {
            Assert.That(PropPlacement.Problems(room), Is.Empty, room.Id);
        }
    }

    [Test]
    public void TownSquareAndTheCafe_EachHaveTheirOwnProps()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RoomCatalog.Find("town-square")!.Props.Select(p => p.Kind),
                Is.SupersetOf(new[] { PropKinds.Bench, PropKinds.Lamp, PropKinds.Tree, PropKinds.FlowerBed }));
            Assert.That(RoomCatalog.Find("cafe")!.Props.Select(p => p.Kind),
                Is.SupersetOf(new[] { PropKinds.CafeTable, PropKinds.Counter, PropKinds.Plant }));
        });
    }

    [Test]
    public void APropOutOfTheWay_IsFine()
    {
        Assert.That(PropPlacement.Problems(RoomWith(Bench(7, -4))), Is.Empty);
    }

    [Test]
    public void APropPartlyOffTheFloor_IsAProblem()
    {
        Assert.That(PropPlacement.Problems(RoomWith(Bench(9.5, -4))), Has.One.Contains("off the floor"));
    }

    [Test]
    public void APropOnADoor_IsAProblem()
    {
        Assert.That(PropPlacement.Problems(RoomWith(Bench(7.5, 4.5, radius: 0.5))), Has.Some.Contains("on the door out"));
    }

    [Test]
    public void APropWhereCatsFish_IsAProblem_NotJustOneInTheWater()
    {
        // Clear of the water (1.5) by more than its own size, but within reach of a line from the edge.
        Assert.That(PropPlacement.Problems(RoomWith(Bench(-5, -2.2, radius: 0.5))),
            Has.Some.Contains("in the pond or where cats fish from"));
    }

    [Test]
    public void APropOnTheSpawnPoint_IsAProblem()
    {
        Assert.That(PropPlacement.Problems(RoomWith(Bench(0.5, -3.5, radius: 0.3))), Has.Some.Contains("on the spawn point"));
    }

    [Test]
    public void APropOnTheWayInByADoor_IsAProblem()
    {
        Assert.That(PropPlacement.Problems(RoomWith(Bench(Out.Arrival.X, Out.Arrival.Z - 0.5, radius: 0.3))),
            Has.Some.Contains("on the way in by the door out"));
    }

    [Test]
    public void APropOnTheWalkFromTheSpawnPointToTheDoor_IsAProblem()
    {
        // Halfway along the straight walk from (0, -3) to (6, 4), and away from both ends.
        Assert.That(PropPlacement.Problems(RoomWith(Bench(3, 0.5, radius: 0.5))),
            Has.Some.Contains("on the walk from the spawn point to the door out"));
    }

    [Test]
    public void APropOnTheWalkFromTheSpawnPointToThePond_IsAProblem()
    {
        Assert.That(PropPlacement.Problems(RoomWith(Bench(-2.5, -1, radius: 0.3))),
            Has.Some.Contains("on the walk from the spawn point to the pond"));
    }

    [Test]
    public void PropsOnEachOther_AreAProblem()
    {
        Assert.That(PropPlacement.Problems(RoomWith(Bench(7, -4), Bench(8, -4.5))), Has.Some.Contains("overlaps the bench"));
    }

    [Test]
    public void APropOfAKindTheBrowserDoesNotDraw_IsAProblem()
    {
        var statue = new Prop("statue", new Position(7, -4), Radius: 1);

        Assert.That(PropPlacement.Problems(RoomWith(statue)), Has.One.Contains("no kind of prop"));
    }

    [Test]
    public void APropWithNoSize_IsAProblem()
    {
        Assert.That(PropPlacement.Problems(RoomWith(Bench(7, -4, radius: 0))), Has.One.Contains("has no size"));
    }
}

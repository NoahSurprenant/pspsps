using Pspsps.Domain.Rooms;

namespace Pspsps.Server.Test;

/// <summary>The cases <see cref="BackdropClearance"/> is there to catch, shown on a bare 14 x 10 room with its spawn in the middle and one door in the north edge.</summary>
public sealed class BackdropClearanceTests
{
    private static readonly RoomLayout Room = new("Test", 14, 10, new Position(0, 0), 10)
    {
        Doors = [new DoorLayout("north", RoomEdge.North, 0, "elsewhere", "south")],
    };

    private static PropPart Boulder(double x, double z, double diameter = 1) =>
        new("ball", [x, diameter * 0.3, z], "#a39a8a", Diameter: diameter, Height: diameter * 0.6);

    private static IEnumerable<string> Problems(params PropPart[] parts) => BackdropClearance.Problems("test", Room, parts);

    [Test]
    public void ABoulder_InTheMiddleOfTheFloor_AwayFromEveryWalkAndProp_IsOnTheWalkableFloor() =>
        Assert.That(Problems(Boulder(4, 2)), Has.Some.Contains("walkable floor"));

    [Test]
    public void ABoulder_ABitInsideTheFloorEdge_IsOnTheWalkableFloor() =>
        Assert.That(Problems(Boulder(6.2, 0.5)), Has.Some.Contains("walkable floor"));

    [Test]
    public void ABoulder_AgainstTheWall_IsFine() =>
        Assert.That(Problems(Boulder(6.5, 0.5)), Is.Empty);

    [Test]
    public void ALowMarkOnTheFloor_AndAnOverheadBeam_AreFineAnywhere() =>
        Assert.That(Problems(
            new PropPart("box", [3, 0.01, 2], "#fff", Width: 2, Height: 0.02, Depth: 2),
            new PropPart("box", [0, 3.5, 2], "#fff", Width: 2, Height: 0.2, Depth: 2)), Is.Empty);

    [Test]
    public void AHillWhoseBoxCoversTheFloorButWhoseRoundBodyDoesNot_IsScenery() =>
        Assert.That(Problems(new PropPart("ball", [-12, 5, 8], "#9bbcae", Diameter: 14, Height: 10)), Is.Empty);

    [Test]
    public void ACylinderThatFlaresWider_IsMeasuredByItsWiderEnd()
    {
        var narrow = new PropPart("cylinder", [6.5, 0.5, 0.5], "#fff", Diameter: 0.4, Height: 1);
        var flared = narrow with { Top = 2 };

        Assert.Multiple(() =>
        {
            Assert.That(Problems(narrow), Is.Empty);
            Assert.That(Problems(flared), Has.Some.Contains("walkable floor"));
        });
    }

    [Test]
    public void APartOnTheStraightWalkFromTheSpawnToTheDoor_IsOnTheWalk() =>
        Assert.That(Problems(new PropPart("box", [0, 0.5, 3], "#fff", Width: 0.4, Height: 1, Depth: 0.4)), Has.Some.Contains("walk"));
}

using Pspsps.Domain.Players;
using Pspsps.Domain.Rooms;

namespace Pspsps.Domain.Test;

public sealed class DoorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Door Out = new("out", new Position(3, 2), Radius: 0.5, ToRoom: "elsewhere", ToDoor: "in");

    private static readonly RoomDefinition Room =
        new("test", "Test Room", Width: 10, Depth: 6, Spawn: new(1, -2), Capacity: 2) { Doors = [Out] };

    private static readonly Guid Tom = Guid.NewGuid();
    private const string Tab = "tom-tab";

    [Test]
    public void DistanceTo_IsStraightAcrossTheFloor()
    {
        Assert.That(new Position(1, 2).DistanceTo(new Position(4, 6)), Is.EqualTo(5));
        Assert.That(new Position(-1, -1).DistanceTo(new Position(-1, -1)), Is.Zero);
    }

    [Test]
    public void InReach_OnlyOnTheDoor()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Out.InReach(new Position(3, 2)), Is.True, "its middle");
            Assert.That(Out.InReach(new Position(3.5, 2)), Is.True, "its edge");
            Assert.That(Out.InReach(new Position(3.36, 2.36)), Is.False, "past its edge, on the diagonal");
            Assert.That(Out.InReach(new Position(0, 0)), Is.False, "across the room");
        });
    }

    [Test]
    public void Arrival_IsJustInsideTheDoor_TowardsTheMiddle_AndOffIt()
    {
        var arrival = Out.Arrival;

        var fromDoor = arrival.DistanceTo(Out.Spot);
        Assert.That(fromDoor, Is.EqualTo(Out.Radius + Door.StepIn).Within(1e-9));
        Assert.That(Out.InReach(arrival), Is.False, "a cat coming in doesn't stand on the door");
        Assert.That(arrival.DistanceTo(new Position(0, 0)), Is.LessThan(Out.Spot.DistanceTo(new Position(0, 0))), "inwards");
    }

    [Test]
    public void Arrival_OfADoorInTheMiddle_StepsTowardsTheFront()
    {
        var middle = new Door("middle", new Position(0, 0), Radius: 1, ToRoom: "elsewhere", ToDoor: "in");

        Assert.That(middle.Arrival, Is.EqualTo(new Position(0, -1 - Door.StepIn)));
    }

    [Test]
    public void LeaveThrough_FromOnTheDoor_TakesTheCatOut_AndSaysWhereItLeads()
    {
        var state = new RoomState(Room);
        state.Join(Tom, "Tom", FurColor.Grey, Tab, Now);
        state.Move(Tom, new Position(3.2, 2.1));

        var door = state.LeaveThrough(Tom, Tab, "out");

        Assert.That(door, Is.EqualTo(Out));
        Assert.That(state.Cats, Is.Empty);
    }

    [Test]
    public void LeaveThrough_AwayFromTheDoor_IsRefused_AndTheCatStays()
    {
        var state = new RoomState(Room);
        state.Join(Tom, "Tom", FurColor.Grey, Tab, Now);

        var refused = Assert.Throws<DomainException>(() => state.LeaveThrough(Tom, Tab, "out"));

        Assert.That(refused!.Message, Is.EqualTo("Walk up to the door to go through it."));
        Assert.That(state.Cats.Single().Position, Is.EqualTo(Room.Spawn));
    }

    [Test]
    public void LeaveThrough_ADoorTheRoomDoesNotHave_IsRefused()
    {
        var state = new RoomState(Room);
        state.Join(Tom, "Tom", FurColor.Grey, Tab, Now);
        state.Move(Tom, Out.Spot);

        var refused = Assert.Throws<DomainException>(() => state.LeaveThrough(Tom, Tab, "nowhere"));

        Assert.That(refused!.Message, Is.EqualTo("There's no such door here."));
        Assert.That(state.Cats, Has.Count.EqualTo(1));
    }

    [Test]
    public void LeaveThrough_FromATabThatNoLongerPlaysTheCat_IsRefused()
    {
        var state = new RoomState(Room);
        state.Join(Tom, "Tom", FurColor.Grey, Tab, Now);
        state.Move(Tom, Out.Spot);
        state.Join(Tom, "Tom", FurColor.Grey, "tom-new-tab", Now);

        Assert.Throws<DomainException>(() => state.LeaveThrough(Tom, Tab, "out"));
        Assert.That(state.Cats, Has.Count.EqualTo(1));
    }

    [Test]
    public void LeaveThrough_ACatThatIsNotHere_IsRefused()
    {
        var state = new RoomState(Room);

        Assert.Throws<DomainException>(() => state.LeaveThrough(Tom, Tab, "out"));
    }

    [Test]
    public void Join_ThroughADoor_PutsTheCatJustInsideIt_NotAtTheSpawnPoint()
    {
        var state = new RoomState(Room);

        var cat = state.Join(Tom, "Tom", FurColor.Grey, Tab, Now, throughDoor: "out");

        Assert.That(cat.Position, Is.EqualTo(Out.Arrival));
    }

    [Test]
    public void Join_ThroughADoorTheRoomDoesNotHave_IsRefused()
    {
        var state = new RoomState(Room);

        Assert.Throws<DomainException>(() => state.Join(Tom, "Tom", FurColor.Grey, Tab, Now, throughDoor: "nowhere"));
        Assert.That(state.Cats, Is.Empty);
    }

    [Test]
    public void TownSquareAndTheCafe_HaveADoorToEachOther()
    {
        var square = RoomCatalog.Find("town-square")!;
        var cafe = RoomCatalog.Find("cafe")!;

        Assert.That(square.Doors.Select(d => (d.ToRoom, d.ToDoor)), Does.Contain(("cafe", "town-square")));
        Assert.That(cafe.Doors.Select(d => (d.ToRoom, d.ToDoor)), Does.Contain(("town-square", "cafe")));
    }

    [Test]
    public void EveryDoor_LeadsToADoorThatLeadsBack_AndIsOnTheFloor_AndArrivingStandsOnNoDoor()
    {
        foreach (var room in RoomCatalog.All)
        {
            foreach (var door in room.Doors)
            {
                var to = RoomCatalog.Find(door.ToRoom);
                Assert.That(to, Is.Not.Null, $"{room.Id}/{door.Id} leads to a room");
                var back = to!.GetDoor(door.ToDoor);
                Assert.That((back.ToRoom, back.ToDoor), Is.EqualTo((room.Id, door.Id)), $"{to.Id}/{back.Id} leads back");

                Assert.That(room.Clamp(door.Spot), Is.EqualTo(door.Spot), $"{room.Id}/{door.Id} is on the floor");
                Assert.That(room.Clamp(door.Arrival), Is.EqualTo(door.Arrival), $"arriving by {room.Id}/{door.Id} is on the floor");
                Assert.That(room.Doors.Where(d => d.InReach(door.Arrival)), Is.Empty, $"arriving by {room.Id}/{door.Id}");
                Assert.That(room.Pond?.InReach(door.Spot) ?? false, Is.False, $"{room.Id}/{door.Id} is away from the pond");
            }

            Assert.That(room.Doors.Select(d => d.Id), Is.Unique, room.Id);
        }
    }
}

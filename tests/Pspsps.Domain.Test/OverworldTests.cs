using Pspsps.Domain.Players;
using Pspsps.Domain.Rooms;

namespace Pspsps.Domain.Test;

public sealed class OverworldTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly ChatFilter Open = ChatFilter.Create([]);

    // "west" is 20 m square at the grid's origin, "east" is its neighbour: world x 0..20 and 20..40, z 0..20.
    private static RoomDefinition West() =>
        new RoomDefinition("west", "West", 20, 20, new Position(0, 0), 100) { Place = new WorldPlace(0, 0) }
            .WithDoors(("in", RoomEdge.South, 0, "hall", "out"));

    private static RoomDefinition East() =>
        new("east", "East", 20, 20, new Position(0, 0), 100) { Place = new WorldPlace(10, 0) };

    private static WorldSpace Space() => new([West(), East(), new RoomDefinition("indoors", "Indoors", 8, 8, new Position(0, 0), 5)]);

    private static (OverworldState World, Guid Id) WithCat(OverworldState? world = null, string room = "west", string? door = null)
    {
        world ??= new OverworldState(Space(), 300);
        var id = Guid.NewGuid();
        world.Join(id, "Cat", FurColor.Orange, "s-" + id, Now, room, door);
        return (world, id);
    }

    [Test]
    public void OnlyRoomsWithAPlace_AreInTheWorld_AndTheirFloorsJoinUp()
    {
        var space = Space();

        Assert.Multiple(() =>
        {
            Assert.That(space.Rooms.Select(r => r.Id), Is.EqualTo(new[] { "east", "west" }));
            Assert.That(space.Has("indoors"), Is.False);
            Assert.That(space.Contains(new Position(19.9, 10)) && space.Contains(new Position(20.1, 10)), Is.True, "no gap at the seam");
            Assert.That(space.Contains(new Position(41, 10)), Is.False);
            Assert.That(space.RoomAt(new Position(30, 10))!.Id, Is.EqualTo("east"));
            Assert.That(space.ToWorld("east", new Position(0, 0)), Is.EqualTo(new Position(30, 10)));
            Assert.That(space.ToLocal("east", new Position(30, 10)), Is.EqualTo(new Position(0, 0)));
        });
    }

    [Test]
    public void ACat_WalksAcrossTheSeamBetweenRooms_ButStopsAtTheWorldsEdge()
    {
        var (world, id) = WithCat();

        var across = world.Move(id, new Position(35, 5));
        var past = world.Move(id, new Position(60, 5));
        var corner = world.Move(id, new Position(-5, -5));

        Assert.Multiple(() =>
        {
            Assert.That(across, Is.EqualTo(new Position(35, 5)));
            Assert.That(past, Is.EqualTo(new Position(40, 5)));
            Assert.That(corner, Is.EqualTo(new Position(0, 0)));
        });
    }

    [Test]
    public void ACatJoining_StandsAtItsRoomsSpawn_InWorldMetres()
    {
        var (world, id) = WithCat(room: "east");

        Assert.That(world.Cats.Single(c => c.PlayerId == id).Position, Is.EqualTo(new Position(30, 10)));
    }

    [Test]
    public void ARoomThatIsNotOnTheMap_CannotBeJoined()
    {
        var world = new OverworldState(Space(), 300);

        Assert.That(() => world.Join(Guid.NewGuid(), "Cat", FurColor.Orange, "s", Now, "indoors"), Throws.TypeOf<DomainException>());
    }

    [Test]
    public void TheWorldCap_IsTheOnlyLimit_AcrossEveryRoom()
    {
        var world = new OverworldState(Space(), 2);
        WithCat(world, "west");
        WithCat(world, "east");

        Assert.That(() => WithCat(world, "west"), Throws.TypeOf<DomainException>().With.Message.Contains("full"));
    }

    [Test]
    public void AnotherTab_TakesTheCatOver_WithoutMovingIt()
    {
        var (world, id) = WithCat();
        world.Move(id, new Position(30, 5));

        world.Join(id, "Cat", FurColor.Orange, "newer", Now, "west");

        Assert.Multiple(() =>
        {
            Assert.That(world.Cats.Single().Position, Is.EqualTo(new Position(30, 5)));
            Assert.That(world.Leave(id, "older"), Is.False);
            Assert.That(world.Has(id, "newer"), Is.True);
        });
    }

    [Test]
    public void ACat_IsShownOnlyTheNearestOnesWithinViewDistance()
    {
        var world = new OverworldState(Space(), 300);
        var (_, me) = WithCat(world);
        world.Move(me, new Position(0, 0));
        var near = new List<Guid>();
        for (var i = 0; i < 70; i++)
        {
            var (_, other) = WithCat(world);
            world.Move(other, new Position(1 + (i * 0.1), 1));
            near.Add(other);
        }

        var (_, far) = WithCat(world, "east");
        world.Move(far, new Position(39, 19));

        var seen = world.VisibleTo(me);

        Assert.Multiple(() =>
        {
            Assert.That(seen, Has.Count.EqualTo(WorldSpace.MaxVisibleCats));
            Assert.That(seen.Select(c => c.PlayerId), Does.Not.Contain(far), "out of view");
            Assert.That(seen.Select(c => c.PlayerId), Does.Not.Contain(me));
            Assert.That(seen.Select(c => c.PlayerId), Is.EquivalentTo(near.Take(WorldSpace.MaxVisibleCats)), "the nearest 60");
        });
    }

    [Test]
    public void AChatLine_IsHeardByEveryoneWithinViewDistance_AcrossARoomSeam_AndLoggedForThem()
    {
        var (world, speaker) = WithCat();
        world.Move(speaker, new Position(0, 0));
        var (_, neighbour) = WithCat(world, "east");
        world.Move(neighbour, new Position(30, 10));
        var (_, distant) = WithCat(world, "east");
        world.Move(distant, new Position(39.9, 19.9));

        var (line, heard) = world.Say(speaker, "  hello  ", Open, Now);

        Assert.Multiple(() =>
        {
            Assert.That(line.Text, Is.EqualTo("hello"));
            Assert.That(heard, Is.EquivalentTo(new[] { speaker, neighbour }));
            Assert.That(world.HeardBy(neighbour).Select(h => h.Line.Text), Is.EqualTo(new[] { "hello" }));
            Assert.That(world.HeardBy(neighbour).Single().From, Is.EqualTo(new Position(0, 0)));
            Assert.That(world.HeardBy(distant), Is.Empty, "too far to hear it");
        });
    }

    [Test]
    public void WhatAPlayerHeard_KeepsTheLatestLinesOnly()
    {
        var (world, speaker) = WithCat();
        var (_, listener) = WithCat(world);

        for (var i = 0; i < OverworldState.HeardHistory + 5; i++)
        {
            world.Say(speaker, $"line {i}", Open, Now);
        }

        var heard = world.HeardBy(listener);

        Assert.Multiple(() =>
        {
            Assert.That(heard, Has.Count.EqualTo(OverworldState.HeardHistory));
            Assert.That(heard[^1].Line.Text, Is.EqualTo($"line {OverworldState.HeardHistory + 4}"));
        });
    }

    [Test]
    public void ADoor_IsGoneThroughOnlyFromItsSpot_InWorldMetres()
    {
        var (world, id) = WithCat();
        world.Move(id, new Position(10, 10));
        Assert.That(() => world.LeaveThrough(id, "s-" + id, "west", "in"), Throws.TypeOf<DomainException>());

        world.Move(id, world.Space.DoorSpot("west", "in"));
        var door = world.LeaveThrough(id, "s-" + id, "west", "in");

        Assert.Multiple(() =>
        {
            Assert.That(door.ToRoom, Is.EqualTo("hall"));
            Assert.That(world.Cats, Is.Empty);
        });
    }

    [Test]
    public void ACat_ComesInThroughADoorJustInsideIt()
    {
        var (world, id) = WithCat(door: "in");

        var spot = world.Space.DoorSpot("west", "in");

        Assert.That(world.Cats.Single(c => c.PlayerId == id).Position.DistanceTo(spot), Is.GreaterThan(0.8));
    }

    [Test]
    public void ALapsedLease_TakesTheCatOut_ARenewalKeepsIt()
    {
        var (world, kept) = WithCat();
        var (_, lost) = WithCat(world);
        world.Renew(kept, "s-" + kept, Now.AddSeconds(90));

        var expired = world.RemoveExpired(Now.AddSeconds(150));

        Assert.Multiple(() =>
        {
            Assert.That(expired, Is.EqualTo(new[] { lost }));
            Assert.That(world.Renew(lost, "s-" + lost, Now.AddSeconds(150)), Is.EqualTo(CatRenewal.Gone));
        });
    }
}

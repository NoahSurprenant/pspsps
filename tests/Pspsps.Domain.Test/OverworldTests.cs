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

    // A clock that runs a minute on at every step, so a test's walks are not held to a pace.
    private static DateTimeOffset _clock = Now;

    private static Position Walk(OverworldState world, Guid id, Position target) => world.Move(id, target, _clock = _clock.AddMinutes(1));

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

        var across = Walk(world, id, new Position(35, 5));
        var past = Walk(world, id, new Position(60, 5));
        var corner = Walk(world, id, new Position(-5, -5));

        Assert.Multiple(() =>
        {
            Assert.That(across, Is.EqualTo(new Position(35, 5)));
            Assert.That(past, Is.EqualTo(new Position(40, 5)));
            Assert.That(corner.X, Is.EqualTo(17.5).Within(0.01), "a click past the corner stops where the line leaves the floor, not slid into the corner");
            Assert.That(corner.Z, Is.EqualTo(0).Within(0.01));
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
        Walk(world, id, new Position(30, 5));

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
        Walk(world, me, new Position(0, 0));
        var near = new List<Guid>();
        for (var i = 0; i < 70; i++)
        {
            var (_, other) = WithCat(world);
            Walk(world, other, new Position(1 + (i * 0.1), 1));
            near.Add(other);
        }

        var (_, far) = WithCat(world, "east");
        Walk(world, far, new Position(39, 19));

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
        Walk(world, speaker, new Position(0, 0));
        var (_, neighbour) = WithCat(world, "east");
        Walk(world, neighbour, new Position(30, 10));
        var (_, distant) = WithCat(world, "east");
        Walk(world, distant, new Position(39.9, 19.9));

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
        Walk(world, id, new Position(10, 10));
        Assert.That(() => world.LeaveThrough(id, "s-" + id, "west", "in"), Throws.TypeOf<DomainException>());

        Walk(world, id, world.Space.DoorSpot("west", "in"));
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

    [Test]
    public void ACat_IsOnlyHeardBy_ThoseWhoseScreenItIsOn()
    {
        var (world, listener) = WithCat();
        for (var i = 0; i < WorldSpace.MaxVisibleCats; i++)
        {
            var (_, nearer) = WithCat(world);
            Walk(world, nearer, new Position(11, 10));
        }

        var (_, speaker) = WithCat(world);
        Walk(world, speaker, new Position(20, 10));

        var (_, heard) = world.Say(speaker, "hello", Open, Now);

        Assert.Multiple(() =>
        {
            Assert.That(world.VisibleTo(listener).Select(cat => cat.PlayerId), Does.Not.Contain(speaker), "the speaker is past the sixty nearest");
            Assert.That(heard, Does.Not.Contain(listener), "so the listener doesn't hear a cat their screen doesn't show");
            Assert.That(heard, Does.Contain(speaker));
        });
    }

    // "east" ends at world x 40 and "far" starts at 50: ten metres of grass between them that no cat may cross.
    private static OverworldState WithAGap() =>
        new(new WorldSpace([West(), East(), new RoomDefinition("far", "Far", 20, 20, new Position(0, 0), 100) { Place = new WorldPlace(25, 0) }]), 300);

    [Test]
    public void ACatThatClicksAcrossTheGrassBetweenRooms_WalksToTheEdgeAndStops()
    {
        var (world, id) = WithCat(WithAGap(), "east");

        var stopped = Walk(world, id, new Position(60, 10));

        Assert.Multiple(() =>
        {
            Assert.That(stopped.X, Is.EqualTo(40).Within(0.01), "it stops at the east room's edge");
            Assert.That(stopped.Z, Is.EqualTo(10).Within(0.01));
            Assert.That(world.Space.Contains(stopped), Is.True);
        });
    }

    [Test]
    public void ACatThatClicksPastACorner_StopsWhereItsLineLeavesTheFloor_NotSlidAlongTheEdge()
    {
        var (world, id) = WithCat(WithAGap(), "east");
        var start = world.Find(id)!.Position;

        var stopped = Walk(world, id, new Position(start.X + 15, start.Z + 30));

        Assert.Multiple(() =>
        {
            Assert.That(stopped.X, Is.EqualTo(start.X + 5).Within(0.01), "still on the line it set out along, not slid to the target's x");
            Assert.That(stopped.Z, Is.EqualTo(20).Within(0.01), "stopped at the north edge");
        });
    }

    [Test]
    public void ACatThatClicksAnywhereOnTheFloor_StillWalksThereInOneGo()
    {
        var (world, id) = WithCat(WithAGap(), "west");

        var seam = Walk(world, id, new Position(35, 3));

        Assert.That(seam, Is.EqualTo(new Position(35, 3)), "across the seam between west and east");
    }

    [Test]
    public void ACatThatClicksWithAnAbsurdTarget_IsNotHeldUpWorkingOutWhereItStops()
    {
        var (world, id) = WithCat(WithAGap(), "west");

        var stopped = Walk(world, id, new Position(1e15, 10));

        Assert.That(world.Space.Contains(stopped), Is.True);
    }

    // One long room, 300 m by 20 m, with a pond a cat can fish in; the cat arrives at its middle, world x 150.
    private static OverworldState Long() =>
        new(new WorldSpace([new RoomDefinition("long", "Long", 300, 20, new Position(0, 0), 100)
        {
            Place = new WorldPlace(0, 0),
            Pond = new Pond(new Position(100, 0), 3),
        }]), 300);

    [Test]
    public void ACat_CannotCrossTheWorldFasterThanItsPace_ButAnythingAWalkAcrossTheScreenIsFine()
    {
        var world = Long();
        var id = Guid.NewGuid();
        world.Join(id, "Cat", FurColor.Orange, "s", Now, "long");

        var jump = world.Move(id, new Position(290, 10), Now);
        var later = world.Move(id, new Position(290, 10), Now.AddSeconds(5));
        var allTheWay = world.Move(id, new Position(290, 10), Now.AddSeconds(60));
        var walkOver = world.Move(id, new Position(280, 10), Now.AddSeconds(60));

        Assert.Multiple(() =>
        {
            Assert.That(jump.X, Is.EqualTo(150 + OverworldState.WalkBurst).Within(1e-6), "a jump gets as far as the burst");
            Assert.That(later.X, Is.EqualTo(210 + (5 * OverworldState.MaxWalkSpeed)).Within(1e-6), "then no faster than the pace");
            Assert.That(allTheWay.X, Is.EqualTo(290).Within(1e-6), "a minute later it has got there");
            Assert.That(walkOver.X, Is.EqualTo(280).Within(1e-6), "a short walk is never held back");
        });
    }

    [Test]
    public void ACatAtThePond_CanCastAndReel_ButNotFromAfarOrWithALineOutOrOnceItWalkedOff()
    {
        var world = Long();
        var id = Guid.NewGuid();
        world.Join(id, "Cat", FurColor.Orange, "s", Now, "long");

        Assert.That(() => world.Cast(id, Now, 0.5), Throws.TypeOf<DomainException>().With.Message.Contains("pond"), "the pond is 100 m off");
        world.Move(id, new Position(247, 10), Now);
        world.Move(id, new Position(247, 10), Now.AddMinutes(1));
        Assert.That(world.Find(id)!.Position.X, Is.EqualTo(247).Within(1e-6));
        var line = world.Cast(id, Now.AddMinutes(1), 0.5);

        Assert.Multiple(() =>
        {
            Assert.That(world.LineOf(id), Is.SameAs(line));
            Assert.That(() => world.Cast(id, Now.AddMinutes(2), 0.5), Throws.TypeOf<DomainException>().With.Message.Contains("already"));
            Assert.That(world.Bite(id, line, line.BiteAt), Is.EqualTo("s"), "the bite is told to the cat's tab");
            Assert.That(world.Reel(id, line.BiteAt, 0.1).Outcome, Is.EqualTo(Pspsps.Domain.Fishing.ReelOutcome.Caught));
            Assert.That(world.LineOf(id), Is.Null);
        });

        var second = world.Cast(id, Now.AddMinutes(3), 0.5);
        world.Move(id, new Position(200, 10), Now.AddMinutes(4));
        Assert.Multiple(() =>
        {
            Assert.That(world.LineOf(id), Is.Null, "walking off reels the line in");
            Assert.That(world.Bite(id, second, second.BiteAt), Is.Null, "a bite on a line that is gone is told to nobody");
        });
    }

    [Test]
    public void ACatCanGoToAnother_WhereverItIsInTheWorld_UnlessItIsAlreadyNearby()
    {
        var world = Long();
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        world.Join(a, "A", FurColor.Orange, "sa", Now, "long");
        world.Join(b, "B", FurColor.Orange, "sb", Now, "long");
        world.Move(b, new Position(210, 10), Now);
        world.Move(b, new Position(260, 10), Now.AddMinutes(1));

        var near = world.GoTo(a, b, 4);
        var again = world.GoTo(a, b, 4);

        Assert.Multiple(() =>
        {
            Assert.That(near!.Value.DistanceTo(world.Find(b)!.Position), Is.LessThan(4), "it lands by the friend");
            Assert.That(world.Find(a)!.Position, Is.EqualTo(near));
            Assert.That(again, Is.Null, "already there");
            Assert.That(() => world.GoTo(a, Guid.NewGuid(), 4), Throws.TypeOf<DomainException>());
        });
    }

    [Test]
    public void WhoIsInView_AndWhoHearsALine_IsWhatAFullScanOfTheCrowdGives_WhereverCatsWalkAndLeave()
    {
        var world = new OverworldState(new WorldSpace([new RoomDefinition("big", "Big", 400, 300, new Position(0, 0), 500)
        {
            Place = new WorldPlace(-100, -75),
        }]), 500);
        var random = new Random(7);
        var ids = Enumerable.Range(0, 150).Select(_ => Guid.NewGuid()).ToList();
        foreach (var id in ids)
        {
            world.Join(id, "Cat", FurColor.Orange, id.ToString(), Now, "big");
        }

        for (var round = 0; round < 20; round++)
        {
            foreach (var id in ids.Where(id => world.Find(id) is not null))
            {
                Walk(world, id, new Position(random.NextDouble() * 400 - 200, random.NextDouble() * 300 - 150));
            }

            if (round % 5 == 4)
            {
                var leaving = ids.First(id => world.Find(id) is not null);
                world.Leave(leaving, leaving.ToString());
            }

            foreach (var viewer in ids.Where(id => world.Find(id) is not null).Take(10))
            {
                var me = world.Find(viewer)!;
                var expected = world.Cats
                    .Where(cat => cat.PlayerId != viewer && cat.Position.DistanceTo(me.Position) <= WorldSpace.ViewDistance)
                    .OrderBy(cat => cat.Position.DistanceTo(me.Position)).ThenBy(cat => cat.PlayerId)
                    .Take(WorldSpace.MaxVisibleCats).Select(cat => cat.PlayerId);
                Assert.That(world.VisibleTo(viewer).Select(cat => cat.PlayerId), Is.EqualTo(expected), $"round {round}");
            }
        }

        var speaker = ids.Last(id => world.Find(id) is not null);
        var (_, heard) = world.Say(speaker, "hello", Open, _clock);
        var speakerAt = world.Find(speaker)!.Position;
        var wouldHear = world.Cats
            .Where(cat => cat.PlayerId == speaker || (cat.Position.DistanceTo(speakerAt) <= WorldSpace.ViewDistance
                && world.VisibleTo(cat.PlayerId).Any(seen => seen.PlayerId == speaker)))
            .Select(cat => cat.PlayerId);
        Assert.That(heard, Is.EquivalentTo(wouldHear));
    }
}

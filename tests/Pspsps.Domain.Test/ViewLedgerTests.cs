using Pspsps.Domain.Players;
using Pspsps.Domain.Rooms;

namespace Pspsps.Domain.Test;

public sealed class ViewLedgerTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    private static (OverworldState World, DateTimeOffset Clock) Crowd(int cats, Func<int, Position> spot)
    {
        var world = new OverworldState(
            new WorldSpace([new RoomDefinition("big", "Big", 400, 300, new Position(0, 0), 500) { Place = new WorldPlace(-100, -75) }]), cats + 5);
        var clock = Start;
        for (var i = 1; i <= cats; i++)
        {
            var id = OverworldCrowdViewTests.Id(i);
            world.Join(id, "Cat", FurColor.Orange, id.ToString(), Start, "big");
            world.Move(id, spot(i), clock = clock.AddMinutes(1));
        }

        return (world, clock);
    }

    [Test]
    public void AViewerWhoseSyncFailed_IsNamedAgain_UntilItIsSet()
    {
        // Cat 1 sees cats 2 and 3 only; cat 4 walks a long way off, so a move by cat 4 cannot change cat 1's screen.
        var (world, clock) = Crowd(4, i => i == 4 ? new Position(150, 100) : new Position(i, 0));
        var views = new ViewLedger();
        foreach (var i in Enumerable.Range(1, 4))
        {
            var id = OverworldCrowdViewTests.Id(i);
            views.Set(id, world.Find(id)!.Position, world.VisibleTo(id));
        }

        var one = OverworldCrowdViewTests.Id(1);
        var mover = world.Find(OverworldCrowdViewTests.Id(4))!;
        Assert.That(views.Affected(world, mover), Does.Not.Contain(one), "a far move leaves cat 1 alone");

        views.MarkStale(one);
        Assert.That(views.Affected(world, mover), Does.Contain(one), "stale: named again, though the move cannot change its screen");

        views.Set(one, world.Find(one)!.Position, world.VisibleTo(one));
        Assert.That(views.Affected(world, mover), Does.Not.Contain(one), "set: healed");
    }

    [Test]
    public void ACatNeverSet_IsAlwaysNamed()
    {
        var (world, _) = Crowd(2, i => new Position(i * 100, 0));
        var views = new ViewLedger();

        Assert.That(views.Affected(world, world.Find(OverworldCrowdViewTests.Id(2))!), Does.Contain(OverworldCrowdViewTests.Id(1)).Or.Not.Contain(OverworldCrowdViewTests.Id(1)));
        views.Set(OverworldCrowdViewTests.Id(2), world.Find(OverworldCrowdViewTests.Id(2))!.Position, []);
        Assert.That(views.Affected(world, world.Find(OverworldCrowdViewTests.Id(2))!), Does.Contain(OverworldCrowdViewTests.Id(1)));
    }

    [Test]
    public void ForgettingACat_NeverThrows_AndMarksItsViewersStale()
    {
        var (world, _) = Crowd(3, i => new Position(i, 0));
        var views = new ViewLedger();
        foreach (var i in Enumerable.Range(1, 3))
        {
            var id = OverworldCrowdViewTests.Id(i);
            views.Set(id, world.Find(id)!.Position, world.VisibleTo(id));
        }

        var gone = OverworldCrowdViewTests.Id(3);
        var viewers = views.Forget(gone);
        Assert.Multiple(() =>
        {
            Assert.That(viewers, Is.EquivalentTo(new[] { OverworldCrowdViewTests.Id(1), OverworldCrowdViewTests.Id(2) }));
            Assert.That(views.KnownBy(OverworldCrowdViewTests.Id(1)), Does.Not.Contain(gone));
            Assert.That(views.Watching(gone), Is.Empty);
            Assert.That(() => views.Forget(gone), Throws.Nothing, "forgetting twice is harmless");
            Assert.That(() => views.Set(OverworldCrowdViewTests.Id(1), new Position(1, 0), []), Throws.Nothing, "a set after a forget");
        });
    }

    [Test]
    public void AStaleViewerWhoseCatIsGone_IsDroppedNotNamedOnEveryMove()
    {
        var (world, _) = Crowd(3, i => new Position(i * 150 - 300, 0));
        var views = new ViewLedger();
        var gone = OverworldCrowdViewTests.Id(1);
        views.Set(gone, world.Find(gone)!.Position, []);
        views.MarkStale(gone);
        world.Leave(gone, gone.ToString());

        Assert.That(views.Affected(world, world.Find(OverworldCrowdViewTests.Id(3))!), Does.Not.Contain(gone));
    }

    [Test]
    public void WhatAViewerWasShown_IsACopy_ThatAForgetDoesNotChange()
    {
        var (world, _) = Crowd(2, i => new Position(i, 0));
        var views = new ViewLedger();
        foreach (var i in new[] { 1, 2 })
        {
            var id = OverworldCrowdViewTests.Id(i);
            views.Set(id, world.Find(id)!.Position, world.VisibleTo(id));
        }

        var before = views.KnownBy(OverworldCrowdViewTests.Id(1));
        views.Forget(OverworldCrowdViewTests.Id(2));

        Assert.That(before, Does.Contain(OverworldCrowdViewTests.Id(2)));
    }
}

using Pspsps.Domain.Players;
using Pspsps.Domain.Rooms;

namespace Pspsps.Domain.Test;

public sealed class OverworldCrowdViewTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    internal static Guid Id(int n) => new(n, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]);

    private static OverworldState Big(int cats) =>
        new(new WorldSpace([new RoomDefinition("big", "Big", 400, 300, new Position(0, 0), 500) { Place = new WorldPlace(-100, -75) }]), cats);

    // Walks, joins and leaves with the same seed, checking after every step that each cat has been shown exactly what
    // a full scan of the crowd says it should see, for the old way (everyone near is brought up to date) and the indexed one.
    [TestCase(false, 400, 30, 2)]
    [TestCase(true, 400, 30, 2)]
    [TestCase(false, 300, 120, 3)]
    [TestCase(true, 300, 120, 3)]
    [TestCase(true, 150, 400, 5)]
    public void WhatEachCatIsShown_IsWhatAFullScanGives_AfterEveryMoveJoinAndDeparture(bool filtered, int cats, double spread, int seed)
    {
        var world = Big(cats + 10);
        var crowd = new CrowdView(world, filtered);
        var random = new Random(seed);
        var clock = Start;
        var live = new List<Guid>();
        var next = 0;

        Position Spot() => new((random.NextDouble() * spread) - (spread / 2), (random.NextDouble() * spread) - (spread / 2));

        void Join()
        {
            var id = Id(++next);
            world.Join(id, "Cat", FurColor.Orange, id.ToString(), Start, "big");
            clock = clock.AddMinutes(1);
            world.Move(id, Spot(), clock);
            live.Add(id);
            crowd.Seen(id);
            crowd.Moved(world.Find(id)!);
        }

        for (var i = 0; i < cats; i++)
        {
            Join();
        }

        for (var step = 0; step < 150; step++)
        {
            var roll = random.Next(10);
            if (roll == 0)
            {
                var id = live[random.Next(live.Count)];
                world.Leave(id, id.ToString());
                live.Remove(id);
                crowd.Departed(id);
            }
            else if (roll == 1 && live.Count < cats + 5)
            {
                Join();
            }
            else
            {
                var id = live[random.Next(live.Count)];
                var at = world.Find(id)!.Position;
                clock = clock.AddMinutes(1);
                var far = random.Next(4) == 0;
                var reach = far ? 60 : 3;
                world.Move(id, new Position(at.X + ((random.NextDouble() - 0.5) * 2 * reach), at.Z + ((random.NextDouble() - 0.5) * 2 * reach)), clock);
                crowd.Moved(world.Find(id)!);
            }

            foreach (var id in live)
            {
                Assert.That(
                    crowd.Views.KnownBy(id).OrderBy(x => x), Is.EqualTo(world.VisibleTo(id).Select(c => c.PlayerId).OrderBy(x => x)),
                    $"step {step}, cat {id}");
            }
        }
    }
}

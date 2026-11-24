using System.Diagnostics;
using System.Globalization;
using System.Text;
using Pspsps.Domain.Players;
using Pspsps.Domain.Rooms;

namespace Pspsps.Domain.Test;

/// <summary>
/// Measures what a move costs the open world with a crowd: what the room grain does after each move, old way and indexed
/// (see <see cref="CrowdView"/>). Issue #134. Explicit: a measurement, run it to see the numbers.
/// </summary>
public sealed class OverworldCrowdCostTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    [Test]
    [Explicit("A measurement, not a check: run it to see the numbers.")]
    public void Measure_WhatAMoveCosts_WithACrowd()
    {
        var report = new StringBuilder();
        foreach (var (name, spread) in new[] { ("spread over the whole world", true), ("crowded in one 30 m spot", false) })
        {
            foreach (var cats in new[] { 100, 500, 2000 })
            {
                foreach (var indexed in new[] { false, true })
                {
                    report.AppendLine(Measure(name, spread, cats, indexed));
                }
            }
        }

        Console.WriteLine(report);
    }

    private static string Measure(string name, bool spread, int cats, bool indexed)
    {
        var world = new OverworldState(new WorldSpace([new RoomDefinition("big", "Big", 400, 300, new Position(0, 0), 500)
        {
            Place = new WorldPlace(-100, -75),
        }]), cats);
        var random = new Random(11);
        Position Spot() => spread
            ? new Position((random.NextDouble() * 400) - 200, (random.NextDouble() * 300) - 150)
            : new Position((random.NextDouble() * 30) - 15, (random.NextDouble() * 30) - 15);

        var clock = Start;
        var ids = Enumerable.Range(0, cats).Select(_ => Guid.NewGuid()).ToList();
        var crowd = new CrowdView(world, indexed);
        foreach (var id in ids)
        {
            world.Join(id, "Cat", FurColor.Orange, id.ToString(), Start, "big");
            clock = clock.AddMinutes(1);
            world.Move(id, Spot(), clock);
        }

        foreach (var id in ids)
        {
            crowd.Seen(id);
        }

        void Step()
        {
            var id = ids[random.Next(ids.Count)];
            var at = world.Find(id)!.Position;
            clock = clock.AddMinutes(1);
            world.Move(id, new Position(at.X + (random.NextDouble() * 4) - 2, at.Z + (random.NextDouble() * 4) - 2), clock);
            crowd.Moved(world.Find(id)!);
        }

        for (var i = 0; i < 5; i++)
        {
            Step();
        }

        var moves = Math.Max(20, 40000 / cats);
        var synced = crowd.Synced;
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < moves; i++)
        {
            Step();
        }

        sw.Stop();
        synced = crowd.Synced - synced;
        var perMove = sw.Elapsed.TotalMilliseconds / moves;

        var leavers = Math.Min(20, cats / 2);
        var leave = Stopwatch.StartNew();
        foreach (var id in ids.Take(leavers))
        {
            world.Leave(id, id.ToString());
            crowd.Departed(id);
        }

        leave.Stop();
        return string.Create(CultureInfo.InvariantCulture,
            $"{(indexed ? "indexed" : "old    ")} | {name}, {cats} cats: {perMove:F2} ms per move (avg {(double)synced / moves:F0} viewers synced; {moves} moves); " +
            $"{leave.Elapsed.TotalMilliseconds / leavers:F2} ms per departure. A round of {cats} moves = {perMove * cats / 1000:F1} s.");
    }
}

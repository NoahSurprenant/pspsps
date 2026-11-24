using System.Diagnostics;
using System.Globalization;
using System.Text;
using Pspsps.Domain.Players;
using Pspsps.Domain.Rooms;

namespace Pspsps.Domain.Test;

/// <summary>
/// Measures what a move costs the open world with a crowd, doing what the room grain does after each move
/// (<c>SyncNear</c>: recompute what every cat near the mover sees, and scan <c>_known</c> for who watched it). Issue #134.
/// </summary>
public sealed class OverworldCrowdCostTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void Measure_WhatAMoveCosts_WithACrowd()
    {
        var report = new StringBuilder();
        foreach (var (name, spread) in new[] { ("spread over the whole world", true), ("crowded in one 30 m spot", false) })
        {
            foreach (var cats in new[] { 100, 500, 2000 })
            {
                report.AppendLine(Measure(name, spread, cats));
            }
        }

        Console.WriteLine(report);
        Assert.Fail("Measurement (remove this failure once read):\n" + report);
    }

    private static string Measure(string name, bool spread, int cats)
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
        var known = new Dictionary<Guid, HashSet<Guid>>();
        foreach (var id in ids)
        {
            world.Join(id, "Cat", FurColor.Orange, id.ToString(), Start, "big");
            clock = clock.AddMinutes(1);
            world.Move(id, Spot(), clock);
        }

        foreach (var id in ids)
        {
            known[id] = [.. world.VisibleTo(id).Select(c => c.PlayerId)];
        }

        var moves = Math.Max(10, 20000 / cats);
        var average = 0.0;
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < moves; i++)
        {
            var id = ids[random.Next(ids.Count)];
            var at = world.Find(id)!.Position;
            clock = clock.AddMinutes(1);
            var cat = world.Find(id)!;
            world.Move(id, new Position(at.X + (random.NextDouble() * 4) - 2, at.Z + (random.NextDouble() * 4) - 2), clock);
            cat = world.Find(id)!;
            average += SyncNear(world, known, cat);
        }

        sw.Stop();
        var perMove = sw.Elapsed.TotalMilliseconds / moves;

        // Cats leaving: the scan of _known for who knew them.
        var leavers = Math.Min(20, cats / 2);
        var leave = Stopwatch.StartNew();
        for (var i = 0; i < leavers; i++)
        {
            var id = ids[i];
            var viewers = known.Where(e => e.Value.Contains(id)).Select(e => e.Key).ToList();
            known.Remove(id);
            foreach (var k in known.Values)
            {
                k.Remove(id);
            }

            world.Leave(id, id.ToString());
            foreach (var viewer in viewers.Where(v => world.Find(v) is not null))
            {
                known[viewer] = [.. world.VisibleTo(viewer).Select(c => c.PlayerId)];
            }
        }

        leave.Stop();
        return string.Create(CultureInfo.InvariantCulture,
            $"{name}, {cats} cats: {perMove:F2} ms per move (avg {average / moves:F0} viewers synced; {moves} moves); " +
            $"{leave.Elapsed.TotalMilliseconds / leavers:F2} ms per departure ({leavers}). A round of {cats} moves = {perMove * cats / 1000:F1} s.");
    }

    // The grain's SyncNear, without the network: returns how many viewers it brought up to date.
    private static int SyncNear(OverworldState world, Dictionary<Guid, HashSet<Guid>> known, CatInRoom cat)
    {
        var affected = world.CatsAround(cat.Position)
            .Where(other => other.Position.DistanceTo(cat.Position) <= WorldSpace.ViewDistance)
            .Select(other => other.PlayerId)
            .Append(cat.PlayerId)
            .Concat(known.Where(entry => entry.Value.Contains(cat.PlayerId)).Select(entry => entry.Key))
            .Distinct()
            .ToList();
        foreach (var viewer in affected)
        {
            var visible = world.VisibleTo(viewer);
            var now = visible.Select(c => c.PlayerId).ToHashSet();
            var before = known.GetValueOrDefault(viewer) ?? [];
            known[viewer] = now;
            _ = before.Count(id => !now.Contains(id)) + visible.Count(c => !before.Contains(c.PlayerId));
        }

        return affected.Count;
    }
}

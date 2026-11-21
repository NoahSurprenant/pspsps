using System.Text.Json;
using Pspsps.Application.Rooms;
using Pspsps.Domain.Rooms;

namespace Pspsps.Server.Test;

/// <summary>The checks a pull request that changes ContentDrafts.json runs: whatever it holds must be publishable on top of the rooms and looks the game starts with.</summary>
public sealed class ContentDraftsTests
{
    private static readonly ContentFile Content = ContentFile.Embedded();

    [Test]
    public void TheRoomsInTheFile_TogetherWithTheStartingOnes_AreAValidBook()
    {
        var layouts = RoomCatalog.All.ToDictionary(room => room.Id, RoomLayout.Of);
        foreach (var room in Content.RoomList)
        {
            layouts[room.Id] = room.Layout;
        }

        Assert.That(RoomBook.Problems(layouts, []), Is.Empty);
    }

    [Test]
    public void EveryRoomInTheFile_HasALookToBePaintedWith_ThatTheBrowserCanDraw()
    {
        var starting = RoomLookSeeder.StartingLooks().ToDictionary(l => l.Id, l => l.Look);
        var looks = new Dictionary<string, RoomLook>(starting);
        foreach (var look in Content.LookList)
        {
            looks[look.Id] = look.Look;
        }

        var names = LookNames.BuiltIn.With(Content.PainterList.Select(p => (p.Surface, p.Id)), Content.WildlifeList.Select(w => w.Id));
        var problems = Content.RoomList.Where(room => !looks.ContainsKey(room.Layout.Look ?? room.Id)).Select(room => $"{room.Id}: has no look")
            .Concat(Content.LookList.SelectMany(look => look.Look.Problems(look.Id, names)))
            .Concat(Content.PainterList.SelectMany(p => LookRecipes.PainterProblems(p.Id, p.Surface, p.Steps)))
            .Concat(Content.WildlifeList.SelectMany(w => LookRecipes.WildlifeProblems(w.Id, w.Parts, w.Motion)));

        Assert.That(problems, Is.Empty);
    }

    [Test]
    public void MarketRow_IsOnTheMap_AndEveryDoorBetweenItsRoomsHasAMatchingOneBack()
    {
        var layouts = Content.RoomList.ToDictionary(room => room.Id, room => room.Layout);
        Assert.Multiple(() =>
        {
            Assert.That(IslandMap.Places.Single(place => place.Id == "market-row").Room, Is.EqualTo("market-row"));
            foreach (var (room, other) in new[] { ("town-square", "market-row"), ("market-row", "pizza-parlor"), ("market-row", "night-club") })
            {
                var there = layouts[room].Doors.Single(door => door.ToRoom == other);
                var back = layouts[other].Doors.Single(door => door.ToRoom == room);
                Assert.That((there.ToDoor, back.ToDoor), Is.EqualTo((back.Id, there.Id)), $"{room} and {other} name each other's doors");
            }

            // A shop door in the street is under its own sign: the way back out of a shop stands at the same x along the street.
            foreach (var shop in new[] { "pizza-parlor", "night-club" })
            {
                var street = layouts["market-row"].Doors.Single(door => door.ToRoom == shop);
                var inside = layouts[shop].Doors.Single(door => door.ToRoom == "market-row");
                Assert.That(inside.Offset, Is.EqualTo(street.Offset), $"{shop}'s way out is at the street door's offset");
            }
        });
    }

    [Test]
    public void ThePropDesignsInTheFile_AreTheSeededOnesWhereBothHaveAKind()
    {
        // Loading the file saves a design the database lacks and refuses one that differs from the live design: a seeded copy that drifted from the file's would break it.
        var seeded = PropDesignSeeder.Starting().ToDictionary(d => d.Kind);
        var differing = Content.PropDesignList.Where(d => seeded.ContainsKey(d.Kind))
            .Where(d => JsonSerializer.Serialize((d.Size, d.Parts, d.Growth ?? []), ContentFile.Json) != JsonSerializer.Serialize((seeded[d.Kind].Size, seeded[d.Kind].Parts, seeded[d.Kind].Growth), ContentFile.Json))
            .Select(d => d.Kind);

        Assert.That(differing, Is.Empty);
    }

    [Test]
    public void EveryKindOfProp_HasADesignInTheSeededFileOrTheContentFile()
    {
        // Designs reach an existing database through the content file (the Content workflow); a kind placeable before its design exists would draw as nothing.
        var designed = PropDesignSeeder.Starting().Select(d => d.Kind).Concat(Content.PropDesignList.Select(d => d.Kind)).ToHashSet();

        Assert.That(PropKinds.All.Where(kind => !designed.Contains(kind)), Is.Empty);
    }

    /// <summary>
    /// A cat walks through scenery: a backdrop part that stands on the floor and is taller than a cat's step must keep off the
    /// walkable floor (apart from a 0.4 m band against the wall, for trim) and off every prop. Overhead parts (above a cat) and
    /// low ones (flat marks) are fine.
    /// </summary>
    [TestCase("market-row")]
    [TestCase("pizza-parlor")]
    [TestCase("night-club")]
    public void TheBackdropOf_StaysClearOfTheWalkableFloorAndTheProps(string id)
    {
        const double wallBand = 0.4;
        var layout = Content.RoomList.Single(room => room.Id == id).Layout;
        var parts = Content.LookList.Single(look => look.Id == id).Look.Backdrop.Parts;
        var problems = new List<string>();
        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            var height = part.Height ?? part.Diameter ?? 1;
            if (part.At[1] - (height / 2) >= 0.3 || part.At[1] + (height / 2) <= 0.5)
            {
                continue;
            }

            var halfX = (part.Width ?? part.Diameter ?? 1) / 2;
            var halfZ = (part.Depth ?? part.Diameter ?? 1) / 2;
            if (part.Turn is { Length: 3 } turn && turn[1] != 0)
            {
                halfX = halfZ = Math.Sqrt((halfX * halfX) + (halfZ * halfZ));
            }

            var (x, z) = (part.At[0], part.At[2]);
            if (x + halfX > (-layout.Width / 2) + wallBand && x - halfX < (layout.Width / 2) - wallBand
                && z + halfZ > (-layout.Depth / 2) + wallBand && z - halfZ < (layout.Depth / 2) - wallBand)
            {
                problems.Add($"{id}: part {i} ({part.Shape} at {x:0.0},{z:0.0}) stands on the walkable floor");
            }

            foreach (var prop in layout.Props)
            {
                var nearestX = Math.Clamp(prop.Spot.X, x - halfX, x + halfX);
                var nearestZ = Math.Clamp(prop.Spot.Z, z - halfZ, z + halfZ);
                if (new Position(nearestX, nearestZ).DistanceTo(prop.Spot) < prop.Radius)
                {
                    problems.Add($"{id}: part {i} ({part.Shape} at {x:0.0},{z:0.0}) overlaps the {prop.Kind} at {prop.Spot.X:0.0},{prop.Spot.Z:0.0}");
                }
            }
        }

        Assert.That(problems, Is.Empty);
    }

    [Test]
    public void EveryPropDesignInTheFile_CanBeDrawn() =>
        Assert.That(Content.PropDesignList.SelectMany(d => PropDesign.Problems(d.Kind, d.Size, d.Parts, d.Growth)), Is.Empty);
}

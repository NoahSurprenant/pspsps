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
    public void EveryPropDesignInTheFile_CanBeDrawn() =>
        Assert.That(Content.PropDesignList.SelectMany(d => PropDesign.Problems(d.Kind, d.Size, d.Parts, d.Growth)), Is.Empty);

    [Test]
    public void TheMineShack_IsReachedFromThePearOrchard_AndTheOldTunnelsAreIndoorsBehindItsTunnelDoor()
    {
        var rooms = Content.RoomList.ToDictionary(room => room.Id, room => room.Layout);
        Assert.Multiple(() =>
        {
            Assert.That(rooms.Keys, Does.Contain("mine").And.Contain("mine-tunnels"));
            Assert.That(rooms["pear-orchard"].Doors.Select(d => (d.Id, d.ToRoom, d.ToDoor)),
                Does.Contain(("mine", "mine", "pear-orchard")).And.Contain(("garden", "garden", "pear-orchard")).And.Contain(("riverside", "riverside", "pear-orchard")));
            Assert.That(rooms["mine"].Doors.Select(d => (d.Id, d.ToRoom, d.ToDoor)),
                Does.Contain(("pear-orchard", "pear-orchard", "mine")).And.Contain(("tunnels", "mine-tunnels", "shack")));
            Assert.That(rooms["mine-tunnels"].Doors.Select(d => (d.Id, d.ToRoom, d.ToDoor)), Is.EqualTo(new[] { ("shack", "mine", "tunnels") }));
            Assert.That(IslandMap.Places.Select(p => p.Room), Does.Not.Contain("mine-tunnels"), "indoor rooms are not on the island map");
        });
    }

    [Test]
    public void ThePearOrchardDraft_IsTheLiveOrchardAndTheMineDoor_AndNothingElse()
    {
        var live = RoomLayout.Of(RoomCatalog.All.Single(room => room.Id == "pear-orchard"));
        var draft = Content.RoomList.Single(room => room.Id == "pear-orchard").Layout;
        Assert.Multiple(() =>
        {
            Assert.That((draft.Name, draft.Width, draft.Depth, draft.Spawn, draft.Capacity, draft.Look, draft.Pond),
                Is.EqualTo((live.Name, live.Width, live.Depth, live.Spawn, live.Capacity, live.Look, live.Pond)), "re-copy the live orchard layout");
            Assert.That(draft.Props, Is.EqualTo(live.Props).AsCollection, "re-copy the live orchard layout");
            Assert.That(draft.Doors.Where(door => door.Id != "mine"), Is.EqualTo(live.Doors).AsCollection, "re-copy the live orchard layout");
            Assert.That(draft.Doors.Count(door => door.Id == "mine"), Is.EqualTo(1));
        });
    }

    [Test]
    public void TheMineDoors_AreOnOppositeEdges_NoPropStandsOnADoorMatOrTheSpawn_AndTheLooksAreRich()
    {
        var rooms = Content.RoomList.ToDictionary(room => room.Id, room => room.Layout);
        var orchardDoor = rooms["pear-orchard"].Doors.Single(door => door.Id == "mine");
        var shackDoor = rooms["mine"].Doors.Single(door => door.Id == "pear-orchard");
        Assert.Multiple(() =>
        {
            Assert.That((orchardDoor.Edge, shackDoor.Edge), Is.EqualTo((RoomEdge.West, RoomEdge.East)));
            foreach (var id in new[] { "mine", "mine-tunnels" })
            {
                var room = rooms[id].ToDefinition(id);
                foreach (var prop in room.Props)
                {
                    Assert.That(prop.Spot.DistanceTo(room.Spawn), Is.GreaterThan(prop.Radius), $"{id}: a {prop.Kind} stands on the spawn");
                    foreach (var door in room.Doors)
                    {
                        Assert.That(prop.Spot.DistanceTo(door.Spot), Is.GreaterThan(prop.Radius + door.Radius), $"{id}: a {prop.Kind} stands on the mat of {door.Id}");
                    }
                }
            }

            Assert.That(Content.LookList.Single(look => look.Id == "mine").Look.Backdrop.Parts.Count, Is.GreaterThanOrEqualTo(120), "the mine shack's scenery is thin");
            Assert.That(Content.LookList.Single(look => look.Id == "mine-tunnels").Look.Backdrop.Parts.Count, Is.GreaterThanOrEqualTo(120), "the old tunnels' scenery is thin");
        });
    }

    /// <summary>
    /// A cat walks through scenery: a backdrop part that stands on the floor and is taller than a cat's step must keep off the
    /// walkable floor (apart from a 0.4 m band against the wall, for trim) and off every prop. Overhead parts (above a cat) and
    /// low ones (flat marks, rubble) are fine.
    /// </summary>
    [TestCase("mine")]
    [TestCase("mine-tunnels")]
    public void TheBackdropOf_StaysClearOfTheWalkableFloorAndTheProps(string id)
    {
        const double wallBand = 0.4;
        var layout = Content.RoomList.Single(room => room.Id == id).Layout;
        var parts = Content.LookList.Single(look => look.Id == id).Look.Backdrop.Parts;
        var problems = new List<string>();
        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            var height = part.Height ?? 1;
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
}

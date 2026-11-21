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
        Assert.That(RoomBook.Problems(LayoutsWithTheDrafts(), []), Is.Empty);
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

    /// <summary>The starting rooms with the file's rooms laid over them: the book the game would have once the drafts are published.</summary>
    private static Dictionary<string, RoomLayout> LayoutsWithTheDrafts()
    {
        var layouts = RoomCatalog.All.ToDictionary(room => room.Id, RoomLayout.Of);
        foreach (var room in Content.RoomList)
        {
            layouts[room.Id] = room.Layout;
        }

        return layouts;
    }

    [Test]
    public void TheDojoCourtyard_IsReachedThroughRiverside_AndTheDojoOnlyThroughTheCourtyard()
    {
        var layouts = LayoutsWithTheDrafts();

        Assert.Multiple(() =>
        {
            Assert.That(layouts["riverside"].Doors.Select(d => d.ToRoom), Does.Contain("dojo-courtyard"));
            Assert.That(layouts["dojo-courtyard"].Doors.Select(d => d.ToRoom), Is.EquivalentTo(new[] { "riverside", "dojo" }));
            Assert.That(layouts["dojo"].Doors.Select(d => d.ToRoom), Is.EqualTo(new[] { "dojo-courtyard" }));
            foreach (var room in new[] { "riverside", "dojo-courtyard", "dojo" })
            {
                foreach (var door in layouts[room].Doors.Where(d => d.ToRoom is "dojo-courtyard" or "dojo"))
                {
                    Assert.That(layouts[door.ToRoom].Doors.Any(back => back.Id == door.ToDoor && back.ToRoom == room && back.ToDoor == door.Id),
                        Is.True, $"{room}/{door.Id} leads to {door.ToRoom}/{door.ToDoor}, which must lead back");
                }
            }
        });
    }

    [Test]
    public void TheRiversideDraft_IsTheLiveRoomWithOnlyTheDojoCourtyardDoorAdded()
    {
        var live = RoomLayout.Of(RoomCatalog.All.Single(room => room.Id == "riverside"));
        var draft = Content.RoomList.Single(room => room.Id == "riverside").Layout;
        const string Recopy = "the riverside draft is a copy of the live room: re-copy the live riverside layout from StartingRooms.json into ContentDrafts.json, keeping the dojo-courtyard door";

        Assert.Multiple(() =>
        {
            Assert.That(draft.Name, Is.EqualTo(live.Name), Recopy);
            Assert.That(draft.Width, Is.EqualTo(live.Width), Recopy);
            Assert.That(draft.Depth, Is.EqualTo(live.Depth), Recopy);
            Assert.That(draft.Spawn, Is.EqualTo(live.Spawn), Recopy);
            Assert.That(draft.Capacity, Is.EqualTo(live.Capacity), Recopy);
            Assert.That(draft.Pond, Is.EqualTo(live.Pond), Recopy);
            Assert.That(draft.Look, Is.EqualTo(live.Look), Recopy);
            Assert.That(draft.Props, Is.EqualTo(live.Props), Recopy);
            Assert.That(draft.Doors.Where(door => door.Id != "dojo-courtyard"), Is.EqualTo(live.Doors), Recopy);
        });
    }

    [Test]
    public void TheDojo_IsIndoors_SoItHasNoOpenLandscapeAndIsNotOnTheIslandMap()
    {
        var dojo = Content.LookList.Single(look => look.Id == "dojo").Look;

        Assert.Multiple(() =>
        {
            Assert.That(dojo.OpenLandscape, Is.Not.True);
            Assert.That(IslandMap.Places.Select(place => place.Room), Does.Not.Contain("dojo"));
        });
    }

    [Test]
    public void TheDojoCourtyard_IsTheOnlyDojoPlaceOnTheIslandMap()
    {
        var places = IslandMap.Places.Where(place => place.Room is not null && place.Room.StartsWith("dojo", StringComparison.Ordinal)).ToList();

        Assert.That(places.Select(place => place.Room), Is.EqualTo(new[] { "dojo-courtyard" }));
    }

    [Test]
    public void EveryLookInTheFile_ThatNamesWildlife_HasWhatTheBrowserNeedsToDrawIt()
    {
        var layouts = LayoutsWithTheDrafts();
        var problems = new List<string>();
        foreach (var look in Content.LookList.Where(look => look.Look.Wildlife is not null && Content.RoomList.Any(room => room.Id == look.Id)))
        {
            var layout = layouts[look.Id];
            switch (look.Look.Wildlife)
            {
                case "fish" when layout.Pond is null:
                    problems.Add($"{look.Id}: fish swim in a pond and the room has none");
                    break;
                case "butterfly" when layout.Props.Count(prop => prop.Kind == PropKinds.FlowerBed) < 2:
                    problems.Add($"{look.Id}: butterflies fly between two flower beds and the room has fewer");
                    break;
            }
        }

        Assert.That(problems, Is.Empty);
    }

    /// <summary>Backdrop parts are not part of the prop-placement checks, so this keeps the dojo rooms' decoration off their walks, props and ponds (see <see cref="BackdropClearance"/>).</summary>
    [TestCase("dojo-courtyard")]
    [TestCase("dojo")]
    public void TheBackdropOfTheDojoRooms_KeepsClearOfTheWalksThePropsAndThePond(string id)
    {
        var layout = Content.RoomList.Single(room => room.Id == id).Layout;
        var parts = Content.LookList.Single(look => look.Id == id).Look.Backdrop.Parts;

        Assert.That(BackdropClearance.Problems(id, layout, parts), Is.Empty);
    }

    [Test]
    public void EveryPropDesignInTheFile_CanBeDrawn() =>
        Assert.That(Content.PropDesignList.SelectMany(d => PropDesign.Problems(d.Kind, d.Size, d.Parts, d.Growth)), Is.Empty);

    [Test]
    public void TheCafeDraft_KeepsEverythingTheLiveCafeHas_AndAddsTheStairsToTheBookRoom()
    {
        var live = RoomLayout.Of(RoomCatalog.All.Single(room => room.Id == "cafe"));
        var draft = Content.RoomList.Single(room => room.Id == "cafe").Layout;
        var book = Content.RoomList.Single(room => room.Id == "book-room").Layout;

        const string Drift = "the café draft has drifted from the live café: re-copy the live café layout (StartingRooms.json) into ContentDrafts.json and add only the door";
        Assert.Multiple(() =>
        {
            Assert.That(draft.Name, Is.EqualTo(live.Name), Drift);
            Assert.That(draft.Width, Is.EqualTo(live.Width), Drift);
            Assert.That(draft.Depth, Is.EqualTo(live.Depth), Drift);
            Assert.That(draft.Spawn, Is.EqualTo(live.Spawn), Drift);
            Assert.That(draft.Capacity, Is.EqualTo(live.Capacity), Drift);
            Assert.That(draft.Look, Is.EqualTo(live.Look), Drift);
            Assert.That(draft.Pond, Is.EqualTo(live.Pond), Drift);
            Assert.That(draft.Props, Is.EquivalentTo(live.Props), Drift);
            Assert.That(draft.Doors.Where(d => d.Id != "book-room"), Is.EquivalentTo(live.Doors), Drift);
            Assert.That(draft.Doors.Single(d => d.Id == "book-room").ToRoom, Is.EqualTo("book-room"));
            Assert.That(book.Doors.Single().ToRoom, Is.EqualTo("cafe"));
            Assert.That(IslandMap.Places.Select(p => p.Room), Does.Not.Contain("book-room"));
        });
    }

    // A kind must be in PropKinds.All for a room to place it (PropPlacement), so a new kind goes there together with its
    // design: in PropDesigns.json (a new database) and in ContentDrafts.json's propDesigns (a database that exists).
    [Test]
    public void EveryKindOfPropTheServerPlaces_HasADesign_InTheStartingOnesOrInTheFile()
    {
        var designed = PropDesignSeeder.Starting().Select(d => d.Kind).Concat(Content.PropDesignList.Select(d => d.Kind)).ToHashSet();

        Assert.That(PropKinds.All.Where(kind => !designed.Contains(kind)), Is.Empty);
    }

    private static readonly string[] BookRoomKinds = ["book-stack", "globe", "yarn-nook", "window-seat"];

    [Test]
    public void TheBookRoomsNewDesigns_AreInTheFileAsTheyAreInTheStartingOnes_SoEitherWayOfLoadingGivesTheSameGame()
    {
        var starting = PropDesignSeeder.Starting().ToDictionary(d => d.Kind);
        var text = (object value) => System.Text.Json.JsonSerializer.Serialize(value, ContentFile.Json);

        Assert.Multiple(() =>
        {
            foreach (var kind in BookRoomKinds)
            {
                var drafted = Content.PropDesignList.SingleOrDefault(d => d.Kind == kind);
                Assert.That(drafted, Is.Not.Null, kind);
                Assert.That(drafted!.Size, Is.EqualTo(starting[kind].Size), kind);
                Assert.That(text(drafted.Parts), Is.EqualTo(text(starting[kind].Parts)), kind);
            }
        });
    }

    // The browser draws a design scaled to the prop's circle (its size), outline included; a part that reaches past the
    // size would spill out of the circle the placement rules keep clear. A bound that is never too small: a part
    // turned only about the vertical keeps its footprint's reach, a tilted one its whole diagonal.
    [Test]
    public void EveryDesignInTheFile_DrawsInsideItsSize()
    {
        Assert.Multiple(() =>
        {
            foreach (var design in Content.PropDesignList.Where(d => BookRoomKinds.Contains(d.Kind)))
            {
                var parts = design.Parts.Concat((design.Growth ?? []).SelectMany(step => step.Parts));
                foreach (var (part, index) in parts.Select((p, i) => (p, i)))
                {
                    Assert.That(ReachOf(part), Is.LessThanOrEqualTo(design.Size + 1e-6), $"{design.Kind}, part {index + 1}");
                }
            }
        });
    }

    private static double ReachOf(PropPart part)
    {
        var tilted = part.Turn is { Length: > 0 } turn && (turn[0] != 0 || (turn.Length > 2 && turn[2] != 0));
        var width = part.Width ?? 0;
        var height = part.Height ?? 0;
        var depth = part.Depth ?? 0;
        var diameter = part.Diameter ?? 0;
        var reach = part.Shape switch
        {
            "box" => tilted ? Math.Sqrt((width * width) + (height * height) + (depth * depth)) / 2 : Math.Sqrt((width * width) + (depth * depth)) / 2,
            "cylinder" => tilted ? Math.Sqrt((diameter * diameter) + (height * height)) / 2 : diameter / 2,
            _ => diameter / 2,
        };
        var outline = part.Outlined == false ? 0 : 0.05;
        var x = part.At.Length > 0 ? part.At[0] : 0;
        var z = part.At.Length > 2 ? part.At[2] : 0;
        return Math.Sqrt((x * x) + (z * z)) + reach + outline;
    }

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

    /// <summary>Backdrop parts are not part of the prop-placement checks, so this keeps the mine rooms' decoration off their walks, props and floor flicker (see <see cref="BackdropClearance"/>).</summary>
    [TestCase("mine")]
    [TestCase("mine-tunnels")]
    public void TheBackdropOfTheMineRooms_KeepsClearOfTheWalksAndTheProps(string id)
    {
        var layout = Content.RoomList.Single(room => room.Id == id).Layout;
        var parts = Content.LookList.Single(look => look.Id == id).Look.Backdrop.Parts;

        Assert.That(BackdropClearance.Problems(id, layout, parts), Is.Empty);
    }

    /// <summary>The way in and out must be visible and open: nothing stands on a door's mat or where a cat arrives through it, and the old tunnels' north door and the shack's two doors are all there.</summary>
    [TestCase("mine", "pear-orchard,tunnels")]
    [TestCase("mine-tunnels", "shack")]
    public void TheDoorsOfTheMineRooms_AndWhereCatsArriveThroughThem_AreClear(string id, string doorIds)
    {
        const double clearance = 0.4;
        var layout = Content.RoomList.Single(room => room.Id == id).Layout;
        var parts = Content.LookList.Single(look => look.Id == id).Look.Backdrop.Parts;
        var doors = layout.ToDefinition(id).Doors;
        Assert.That(doors.Select(door => door.Id), Is.EquivalentTo(doorIds.Split(',')));
        var problems = new List<string>();
        foreach (var door in doors)
        {
            foreach (var (what, spot) in new[] { ("the mat", door.Spot), ("the arrival spot", door.Arrival) })
            {
                problems.AddRange(layout.Props.Where(prop => prop.Spot.DistanceTo(spot) < prop.Radius + clearance)
                    .Select(prop => $"{id}: the {prop.Kind} at {prop.Spot.X}, {prop.Spot.Z} is on {what} of {door.Id}"));
                for (var i = 0; i < parts.Count; i++)
                {
                    var part = parts[i];
                    var height = part.Height ?? part.Diameter ?? 0;
                    if (part.At[1] - (height / 2) >= 1 || part.At[1] + (height / 2) <= 0.3)
                    {
                        continue;
                    }

                    // Turned parts reach as far as their diagonal.
                    var halfX = (part.Shape == "box" ? part.Width : part.Diameter).GetValueOrDefault() / 2;
                    var halfZ = (part.Shape == "box" ? part.Depth : part.Diameter).GetValueOrDefault() / 2;
                    if (part.Turn is { Length: 3 } turn && (turn[0] != 0 || turn[1] != 0 || turn[2] != 0))
                    {
                        halfX = halfZ = Math.Sqrt((halfX * halfX) + (halfZ * halfZ));
                    }

                    var nearest = new Position(Math.Clamp(spot.X, part.At[0] - halfX, part.At[0] + halfX), Math.Clamp(spot.Z, part.At[2] - halfZ, part.At[2] + halfZ));
                    if (nearest.DistanceTo(spot) < clearance)
                    {
                        problems.Add($"{id}: backdrop part {i + 1} ({part.Shape} at {part.At[0]}, {part.At[2]}) is on {what} of {door.Id}");
                    }
                }
            }
        }

        Assert.That(problems, Is.Empty);
    }
}

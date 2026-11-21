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
}

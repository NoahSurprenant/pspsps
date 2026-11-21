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
}

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

    /// <summary>Backdrop parts are not part of the prop-placement checks, so this keeps the mine rooms' decoration off their walks, props and floor flicker (see <see cref="BackdropClearance"/>).</summary>
    [TestCase("mine")]
    [TestCase("mine-tunnels")]
    public void TheBackdropOfTheMineRooms_KeepsClearOfTheWalksAndTheProps(string id)
    {
        var layout = Content.RoomList.Single(room => room.Id == id).Layout;
        var parts = Content.LookList.Single(look => look.Id == id).Look.Backdrop.Parts;

        Assert.That(BackdropClearance.Problems(id, layout, parts), Is.Empty);
    }
}

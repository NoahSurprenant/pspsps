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
    public void MarketRow_IsOnTheMap_AndJoinsTownSquareAndTheTwoIndoorRoomsBothWays()
    {
        var layouts = Content.RoomList.ToDictionary(room => room.Id, room => room.Layout);
        var placesWithRooms = IslandMap.Places.Where(place => place.Room is not null).Select(place => place.Room).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(IslandMap.Places.Single(place => place.Id == "market-row").Room, Is.EqualTo("market-row"));
            foreach (var (room, other) in new[] { ("town-square", "market-row"), ("market-row", "pizza-parlor"), ("market-row", "night-club") })
            {
                Assert.That(layouts[room].Doors.Any(door => door.ToRoom == other && door.ToDoor == room), Is.True, $"{room} has a door to {other}");
                Assert.That(layouts[other].Doors.Any(door => door.ToRoom == room && door.ToDoor == other), Is.True, $"{other} has a door back to {room}");
            }

            Assert.That(placesWithRooms, Does.Not.Contain("pizza-parlor"), "a building's inside is not on the map");
            Assert.That(placesWithRooms, Does.Not.Contain("night-club"), "a building's inside is not on the map");
        });
    }

    [Test]
    public void EveryPropInTheFile_IsAKindTheBrowserDraws()
    {
        var unknown = Content.RoomList.SelectMany(room => room.Layout.Props.Select(prop => (room.Id, prop.Kind)))
            .Where(pair => !PropKinds.All.Contains(pair.Kind)).Select(pair => $"{pair.Id}: {pair.Kind}");
        Assert.That(unknown, Is.Empty);
    }

    [Test]
    public void EveryPropDesignInTheFile_CanBeDrawn() =>
        Assert.That(Content.PropDesignList.SelectMany(d => PropDesign.Problems(d.Kind, d.Size, d.Parts, d.Growth)), Is.Empty);
}

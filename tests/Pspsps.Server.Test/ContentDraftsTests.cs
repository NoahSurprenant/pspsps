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
        static string Written(double size, IReadOnlyList<PropPart> parts, IReadOnlyList<PropGrowth>? growth) =>
            JsonSerializer.Serialize(new { size, parts, growth = growth ?? [] }, ContentFile.Json);
        var seeded = PropDesignSeeder.Starting().ToDictionary(d => d.Kind);
        var differing = Content.PropDesignList.Where(d => seeded.ContainsKey(d.Kind))
            .Where(d => Written(d.Size, d.Parts, d.Growth) != Written(seeded[d.Kind].Size, seeded[d.Kind].Parts, seeded[d.Kind].Growth))
            .Select(d => d.Kind);

        Assert.That(differing, Is.Empty);
    }

    [Test]
    public void ADesignThatDriftedFromTheSeededOne_IsCaught()
    {
        // The drift check above must be able to fail: a copy with one part moved differs.
        static string Written(double size, IReadOnlyList<PropPart> parts) => JsonSerializer.Serialize(new { size, parts }, ContentFile.Json);
        var design = PropDesignSeeder.Starting()[0];
        var moved = design.Parts.Select((part, i) => i == 0 ? part with { At = [part.At[0] + 0.1, part.At[1], part.At[2]] } : part).ToList();

        Assert.That(Written(design.Size, moved), Is.Not.EqualTo(Written(design.Size, design.Parts)));
        Assert.That(Written(design.Size + 0.1, design.Parts), Is.Not.EqualTo(Written(design.Size, design.Parts)));
    }

    /// <summary>Backdrop parts are not part of the prop-placement checks, so this keeps the rooms' decoration off their walks, props and ponds (see <see cref="BackdropClearance"/>).</summary>
    [TestCase("market-row")]
    [TestCase("pizza-parlor")]
    [TestCase("night-club")]
    public void TheBackdropOf_KeepsClearOfTheWalksThePropsAndThePond(string id)
    {
        var layout = Content.RoomList.Single(room => room.Id == id).Layout;
        var parts = Content.LookList.Single(look => look.Id == id).Look.Backdrop.Parts;

        Assert.That(BackdropClearance.Problems(id, layout, parts), Is.Empty);
    }

    [Test]
    public void EveryPropDesignInTheFile_CanBeDrawn() =>
        Assert.That(Content.PropDesignList.SelectMany(d => PropDesign.Problems(d.Kind, d.Size, d.Parts, d.Growth)), Is.Empty);
}

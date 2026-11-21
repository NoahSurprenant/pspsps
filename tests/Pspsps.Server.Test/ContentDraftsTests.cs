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

        Assert.That(RoomBook.Problems(layouts, [.. Content.OverlayList]), Is.Empty);
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
    public void TheIceRink_IsReachableFromTownSquare_ThroughSunnyCove()
    {
        var layouts = RoomCatalog.All.ToDictionary(room => room.Id, RoomLayout.Of);
        foreach (var room in Content.RoomList)
        {
            layouts[room.Id] = room.Layout;
        }

        Assert.Multiple(() =>
        {
            Assert.That(layouts["cove"].Doors.Any(door => door.ToRoom == "ice-rink" && door.ToDoor == "cove"), Is.True, "a way from the cove");
            Assert.That(layouts["ice-rink"].Doors.Any(door => door.ToRoom == "cove" && door.ToDoor == "ice-rink"), Is.True, "and back");
            Assert.That(layouts["town-square"].Doors.Any(door => door.ToRoom == "cove"), Is.True, "the cove is reached from the square");
        });
    }

    [TestCase("2027-01-15", true)]
    [TestCase("2027-12-01", true)]
    [TestCase("2027-02-28", true)]
    [TestCase("2027-03-01", false)]
    [TestCase("2027-07-01", false)]
    [TestCase("2027-11-30", false)]
    public void TheIceRink_IsAFrozenRinkInWinter_AndAGrassPitchTheRestOfTheYear(string day, bool winter)
    {
        var layouts = RoomCatalog.All.ToDictionary(room => room.Id, RoomLayout.Of);
        foreach (var room in Content.RoomList)
        {
            layouts[room.Id] = room.Layout;
        }

        var book = RoomBook.Create(layouts, [.. Content.OverlayList]);
        var rink = book.Find("ice-rink", DateTimeOffset.Parse(day, System.Globalization.CultureInfo.InvariantCulture))!;

        Assert.Multiple(() =>
        {
            Assert.That(rink.Props.Count(p => p.Kind == PropKinds.Goal), Is.EqualTo(winter ? 0 : 2), "two goals on the pitch only");
            Assert.That(rink.Props.Any(p => p.Kind == PropKinds.IcePatch), Is.EqualTo(winter), "ice only in winter");
            Assert.That(rink.Props.Any(p => p.Kind == PropKinds.RinkBoard), Is.EqualTo(winter), "boards only in winter");
            Assert.That(rink.Props.Count(p => p.Kind == PropKinds.Bench), Is.GreaterThan(0), "benches all year");
            Assert.That(rink.Props.Count(p => p.Kind == PropKinds.Lamp), Is.GreaterThan(0), "lamps all year");
            Assert.That(rink.Look, Is.EqualTo(winter ? "winter" : null));
        });
    }

    [Test]
    public void EveryOverlayInTheFile_NamesALookVariantThatExists()
    {
        var variants = RoomLookSeeder.StartingVariants().Select(v => v.Name).Concat(Content.VariantList.Select(v => v.Id)).ToHashSet();

        Assert.That(Content.OverlayList.Where(o => o.Look is not null && !variants.Contains(o.Look)).Select(o => o.RoomId + "/" + o.Id), Is.Empty);
    }

    [Test]
    public void EveryPropDesignInTheFile_CanBeDrawn() =>
        Assert.That(Content.PropDesignList.SelectMany(d => PropDesign.Problems(d.Kind, d.Size, d.Parts, d.Growth)), Is.Empty);
}

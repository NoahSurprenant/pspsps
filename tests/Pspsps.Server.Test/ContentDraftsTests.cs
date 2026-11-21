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
        var rink = book.Find("ice-rink", DateTimeOffset.Parse(day, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal))!;

        Assert.Multiple(() =>
        {
            Assert.That(rink.Props.Count(p => p.Kind == PropKinds.Goal), Is.EqualTo(winter ? 0 : 2), "two goals on the pitch only");
            Assert.That(rink.Props.Any(p => p.Kind == PropKinds.IcePatch), Is.EqualTo(winter), "ice only in winter");
            Assert.That(rink.Props.Any(p => p.Kind == PropKinds.RinkBoard), Is.EqualTo(winter), "boards only in winter");
            Assert.That(rink.Props.Count(p => p.Kind == PropKinds.Bench), Is.GreaterThan(0), "benches all year");
            Assert.That(rink.Props.Count(p => p.Kind == PropKinds.Lamp), Is.GreaterThan(0), "lamps all year");
            Assert.That(rink.Look, Is.EqualTo(winter ? "frozen-rink" : null));
        });
    }

    [Test]
    public void EveryOverlayInTheFile_NamesALookVariantThatExists()
    {
        var variants = RoomLookSeeder.StartingVariants().Select(v => v.Name).Concat(Content.VariantList.Select(v => v.Id)).ToHashSet();

        Assert.That(Content.OverlayList.Where(o => o.Look is not null && !variants.Contains(o.Look)).Select(o => o.RoomId + "/" + o.Id), Is.Empty);
    }

    [Test]
    public void TheGoalsAndRinkBoards_FaceTheMiddleOfThePitch()
    {
        var rink = Content.RoomList.Single(room => room.Id == "ice-rink").Layout;
        var winter = Content.OverlayList.Single(o => o.RoomId == "ice-rink" && o.Id == "winter");
        var facing = rink.Props.Concat(winter.Adds).Where(p => p.Kind is PropKinds.Goal or PropKinds.RinkBoard).Where(p => Math.Abs(p.Spot.Z) > 5).ToList();

        Assert.That(facing, Is.Not.Empty);
        foreach (var prop in facing)
        {
            // A prop's front is -z, turned by its facing; the front must point at the middle (the origin, along z).
            var frontZ = -Math.Cos(prop.Facing);
            var towardsMiddleZ = -prop.Spot.Z;
            Assert.That(frontZ * towardsMiddleZ, Is.GreaterThan(0), $"{prop.Kind} at z={prop.Spot.Z} faces away from the pitch");
        }
    }

    [Test]
    public void ThePropDesignsInTheFile_AreTheSameAsTheStartingOnes_SoEitherWayOfLoadingGivesTheSameGame()
    {
        var starting = PropDesignSeeder.Starting().ToDictionary(d => d.Kind);
        var copies = Content.PropDesignList.Where(d => starting.ContainsKey(d.Kind)).ToList();

        Assert.That(copies, Is.Not.Empty);
        foreach (var design in copies)
        {
            var (_, size, parts, growth) = starting[design.Kind];
            Assert.Multiple(() =>
            {
                Assert.That(design.Size, Is.EqualTo(size), $"{design.Kind} size");
                Assert.That(Json(design.Parts), Is.EqualTo(Json(parts)), $"{design.Kind} parts");
                Assert.That(Json(design.Growth ?? []), Is.EqualTo(Json(growth)), $"{design.Kind} growth");
            });
        }
    }

    private static string Json<T>(T value) => System.Text.Json.JsonSerializer.Serialize(value, ContentFile.Json);

    [Test]
    public void EveryVariantFloorInTheFile_NamesAPainterTheBrowserCanDraw()
    {
        var floors = LookNames.BuiltIn.Floors.Concat(Content.PainterList.Where(p => p.Surface == PaintSurfaces.Floor).Select(p => p.Id)).ToHashSet();
        var variants = Content.VariantList.Where(v => v.Floor is not null).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(variants, Is.Not.Empty, "the frozen rink has a floor of its own");
            Assert.That(variants.Where(v => !floors.Contains(v.Floor!.Painter)).Select(v => v.Id), Is.Empty);
            Assert.That(variants.SelectMany(v => new LookVariant(v.Sky, v.WallColour, v.Floor).Problems(v.Id)), Is.Empty);
        });
    }

    [Test]
    public void EveryPropDesignInTheFile_CanBeDrawn() =>
        Assert.That(Content.PropDesignList.SelectMany(d => PropDesign.Problems(d.Kind, d.Size, d.Parts, d.Growth)), Is.Empty);
}

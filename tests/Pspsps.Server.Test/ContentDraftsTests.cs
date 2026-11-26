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
        Assert.That(RoomBook.Problems(LayoutsWithTheDrafts(), [.. Content.OverlayList]), Is.Empty);
    }

    [Test]
    public void EveryRoomInTheFile_HasALookToBePaintedWith_ThatTheBrowserCanDraw()
    {
        var starting = RoomLookSeeder.StartingLooks().ToDictionary(l => l.Id, l => l.Look);
        var looks = new Dictionary<string, RoomLook>(starting);
        foreach (var look in Content.AllLooks)
        {
            looks[look.Id] = look.Look;
        }

        var names = LookNames.BuiltIn.With(Content.PainterList.Select(p => (p.Surface, p.Id)), Content.WildlifeList.Select(w => w.Id));
        var problems = Content.RoomList.Where(room => !looks.ContainsKey(room.Layout.Look ?? room.Id)).Select(room => $"{room.Id}: has no look")
            .Concat(Content.AllLooks.SelectMany(look => look.Look.Problems(look.Id, names)))
            .Concat(Content.PainterList.SelectMany(p => LookRecipes.PainterProblems(p.Id, p.Surface, p.Steps)))
            .Concat(Content.WildlifeList.SelectMany(w => LookRecipes.WildlifeProblems(w.Id, w.Parts, w.Motion)));

        Assert.That(problems, Is.Empty);
    }

    /// <summary>The ground each placed room is drawn with, as the first test resolves a look (the room's own, or the shared one its layout names); null when it has none.</summary>
    private static List<(string Id, LookGround? Ground)> PlacedGrounds()
    {
        var looks = Content.AllLooks.ToDictionary(l => l.Id, l => l.Look);
        return [.. Content.RoomList.Where(room => room.Layout.Place is not null).Select(room =>
            (room.Id, looks.TryGetValue(room.Layout.Look ?? room.Id, out var look) ? look.Ground : null))];
    }

    /// <summary>A ground is water when it is bluer than green; a new placed water room (a lake) is recognised by its ground, and must use the one water colour.</summary>
    private static bool IsWater(LookGround ground) =>
        Convert.ToInt32(ground.Colour[5..], 16) > Convert.ToInt32(ground.Colour[3..5], 16);

    [Test]
    public void PlacedLandRooms_ShareOneGround_SoNoStreetOrColourPatchRunsOnPastTheWorldsEdge()
    {
        var grounds = PlacedGrounds();
        var land = grounds.Where(g => g.Ground is not null && !IsWater(g.Ground)).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(grounds.Where(g => g.Ground is null).Select(g => g.Id), Is.Empty, "every placed room has a ground");
            Assert.That(land.Select(g => g.Ground).Distinct().Count(), Is.EqualTo(1), string.Join(", ", land.Select(g => $"{g.Id}={g.Ground!.Colour}/{g.Ground.Painter}")));
            Assert.That(land.Select(g => g.Ground!.Painter), Has.All.Null, "no cobbled street ground on the open world");
        });
    }

    [Test]
    public void PlacedWaterRooms_ShareOneWaterGround_SoNoSeamShowsAndLandDoesNotCutOffAgainstTheSea()
    {
        var grounds = PlacedGrounds();
        var water = grounds.Where(g => g.Ground is not null && IsWater(g.Ground)).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(water.Select(g => g.Id), Is.SupersetOf(new[] { "dock", "beach", "cove" }), "the sea rooms have a water ground");
            Assert.That(water.Select(g => g.Ground).Distinct().Count(), Is.EqualTo(1), string.Join(", ", water.Select(g => $"{g.Id}={g.Ground!.Colour}")));
            Assert.That(water.Select(g => g.Ground!.Painter), Has.All.Null);
        });
    }

    /// <summary>How many parts, per room, stood over the floor lower than 2 m before this rule was checked here; new scenery adds none (rooms not listed have none).</summary>
    private static readonly Dictionary<string, int> LowPartsAlreadyThere = new()
    {
        ["town-square"] = 22, ["dojo-courtyard"] = 27, ["dojo"] = 44, ["plaza"] = 20, ["pizza-parlor"] = 4, ["mine"] = 19, ["mine-tunnels"] = 55,
        ["book-room"] = 39, ["pet-shop"] = 16, ["hotel"] = 2, ["dock"] = 1, ["peakside-village"] = 5, ["scratching-peaks"] = 11,
    };

    /// <summary>How many parts, per room, lay inside the back wall before it was checked here; new scenery adds none.</summary>
    private static readonly Dictionary<string, int> PartsInTheBackWallAlreadyThere = new()
    {
        ["town-square"] = 10, ["dojo-courtyard"] = 8, ["dojo"] = 2, ["pizza-parlor"] = 2, ["mine"] = 14, ["mine-tunnels"] = 3, ["book-room"] = 4, ["pet-shop"] = 2,
    };

    private static IEnumerable<(string Id, RoomLayout Layout, IReadOnlyList<PropPart> Parts)> BackdropsOfTheRooms() =>
        Content.AllLooks.Where(look => Content.RoomList.Any(room => room.Id == look.Id))
            .Select(look => (look.Id, Content.RoomList.Single(room => room.Id == look.Id).Layout, look.Look.Backdrop.Parts));

    private static void AssertNoMoreThanBefore(Func<string, RoomLayout, IReadOnlyList<PropPart>, IEnumerable<string>> check, Dictionary<string, int> before)
    {
        var worse = BackdropsOfTheRooms().Select(room => (room.Id, Found: check(room.Id, room.Layout, room.Parts).ToList()))
            .Where(room => room.Found.Count > before.GetValueOrDefault(room.Id)).ToList();

        Assert.That(worse.SelectMany(room => room.Found), Is.Empty);
    }

    /// <summary>The browser keeps every room's backdrop off the floor or above the cats' heads (looks.test.ts); the drafts older than that rule keep what they had, but no room adds to it.</summary>
    [Test]
    public void TheBackdropOfEveryRoom_AddsNothingLowerThan2MetresOverTheFloor() =>
        AssertNoMoreThanBefore(BackdropClearance.LowPartsOverTheFloor, LowPartsAlreadyThere);

    /// <summary>The back wall is a slab at the floor's far edge; a part inside it is hidden by it or flickers against it.</summary>
    [Test]
    public void TheBackdropOfEveryRoom_AddsNothingInsideTheBackWall() =>
        AssertNoMoreThanBefore(BackdropClearance.InsideTheBackWall, PartsInTheBackWallAlreadyThere);

    [Test]
    public void TheBackdropOfEveryRoom_StandsInNoProp() =>
        AssertNoMoreThanBefore(BackdropClearance.InsideProps, []);

    /// <summary>Rooms made to look like open scenes stay richly decorated, and keep the scenery they were redrawn with: deleting it for filler fails.</summary>
    [TestCase("dojo", "box", "#7a3a1e", 1, "the fire door")]
    [TestCase("dojo", "box", "#8fa4b4", 1, "the ice door")]
    [TestCase("dojo", "box", "#2f5f6a", 1, "the water door")]
    [TestCase("dojo", "cylinder", "#a8322a", 2, "the red pillars")]
    [TestCase("dojo", "box", "#2f5fa8", 4, "the blue sparring mats")]
    [TestCase("dojo", "ball", "#3b7a46", 1, "the bonsai")]
    [TestCase("forest", "box", "#2f9bd6", 2, "the waterfall and its pool")]
    [TestCase("forest", "box", "#14141a", 1, "the cave mouth")]
    [TestCase("mine", "cylinder", "#2f5fa8", 2, "the volleyball net's poles")]
    [TestCase("mine", "box", "#e8e8ee", 1, "the volleyball net")]
    [TestCase("mine", "box", "#6fb84a", 1, "the picnic blanket")]
    [TestCase("pet-shop", "cylinder", "#e8d48a", 1, "the outer ring of the rug")]
    [TestCase("pet-shop", "cylinder", "#c9e8a0", 1, "the middle ring of the rug")]
    [TestCase("pet-shop", "cylinder", "#a8d4f4", 1, "the inner ring of the rug")]
    public void TheRedrawnRooms_KeepWhatTheyWereRedrawnWith(string id, string shape, string colour, int atLeast, string what)
    {
        var parts = Content.AllLooks.Single(look => look.Id == id).Look.Backdrop.Parts;

        Assert.That(parts.Count(part => part.Shape == shape && part.Colour == colour), Is.GreaterThanOrEqualTo(atLeast), what);
    }

    [Test]
    public void ThePlaza_HasNoGeneratedRowOfHouses_OnlyItsFourBuildings()
    {
        Assert.That(Content.AllLooks.Single(look => look.Id == "plaza").Look.Backdrop.Generator, Is.Null);
    }

    [Test]
    public void TownSquare_HasNoGeneratedRowOfHouses_OnlyItsThreeShops()
    {
        Assert.That(Content.AllLooks.Single(look => look.Id == "town-square").Look.Backdrop.Generator, Is.Null);
    }

    [Test]
    public void TheBeach_KeepsItsSandcastleAndBucket()
    {
        var kinds = Content.RoomList.Single(room => room.Id == "beach").Layout.Props.Select(prop => prop.Kind).ToList();

        Assert.That(kinds, Is.SupersetOf(new[] { "field-sandcastle-small", "field-bucket" }));
    }

    [TestCase("dojo", 100)]
    [TestCase("forest", 95)]
    [TestCase("mine", 120)]
    [TestCase("beach", 65)]
    [TestCase("pet-shop", 70)]
    public void TheRedrawnRooms_KeepARichBackdrop(string id, int atLeast)
    {
        var parts = Content.RoomList.Single(room => room.Id == id).Look!.Backdrop.Parts;

        Assert.That(parts, Has.Count.GreaterThanOrEqualTo(atLeast));
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

    /// <summary>Whether <paramref name="other"/> is placed against the <paramref name="edge"/> of <paramref name="room"/> on the world grid, sharing some of that edge: how two outdoor rooms meet, with no door between them.</summary>
    private static RoomEdge Opposite(RoomEdge edge) => edge switch
    {
        RoomEdge.North => RoomEdge.South,
        RoomEdge.South => RoomEdge.North,
        RoomEdge.East => RoomEdge.West,
        _ => RoomEdge.East,
    };

    /// <summary>Whether <paramref name="other"/> lies against the <paramref name="edge"/> of <paramref name="room"/>: touching it, or at the far end of a path out of that edge (from either room).</summary>
    private static bool Borders(Dictionary<string, RoomLayout> layouts, string room, RoomEdge edge, string other) =>
        Touches(layouts, room, edge, other) || JoinedByPath(layouts, room, edge, other) || JoinedByPath(layouts, other, Opposite(edge), room);

    private static bool JoinedByPath(Dictionary<string, RoomLayout> layouts, string room, RoomEdge edge, string other) =>
        layouts[room].Paths.Any(path => path.Edge == edge && RoomPaths.Reaches(layouts[room].ToDefinition(room), path, layouts[other].ToDefinition(other)));

    private static bool Touches(Dictionary<string, RoomLayout> layouts, string room, RoomEdge edge, string other)
    {
        var (a, b) = (layouts[room], layouts[other]);
        if (a.Place is not { } pa || b.Place is not { } pb)
        {
            return false;
        }

        var (ax, az, bx, bz) = (pa.X * WorldPlace.Cell, pa.Z * WorldPlace.Cell, pb.X * WorldPlace.Cell, pb.Z * WorldPlace.Cell);
        static bool Meets(double a, double b) => Math.Abs(a - b) < 1e-9;
        static bool Shares(double from, double length, double otherFrom, double otherLength) => from < otherFrom + otherLength && otherFrom < from + length;
        return edge switch
        {
            RoomEdge.North => Meets(bz, az + a.Depth) && Shares(ax, a.Width, bx, b.Width),
            RoomEdge.South => Meets(bz + b.Depth, az) && Shares(ax, a.Width, bx, b.Width),
            RoomEdge.East => Meets(bx, ax + a.Width) && Shares(az, a.Depth, bz, b.Depth),
            _ => Meets(bx + b.Width, ax) && Shares(az, a.Depth, bz, b.Depth),
        };
    }

    [Test]
    public void TheDojoCourtyard_IsReachedThroughTheMine_AndTheDojoOnlyThroughTheCourtyard()
    {
        var layouts = LayoutsWithTheDrafts();

        Assert.Multiple(() =>
        {
            Assert.That(Borders(layouts, "mine", RoomEdge.North, "dojo-courtyard"), Is.True, "the courtyard is north of the mine, with no door between");
            Assert.That(layouts["ice-rink"].Doors, Is.Empty, "the rink has no door at all");
            Assert.That(Borders(layouts, "ice-rink", RoomEdge.North, "dojo-courtyard"), Is.False, "and does not border the courtyard");
            Assert.That(layouts["dojo-courtyard"].Doors.Select(d => d.ToRoom), Is.EqualTo(new[] { "dojo" }));
            Assert.That(layouts["dojo"].Doors.Select(d => d.ToRoom), Is.EqualTo(new[] { "dojo-courtyard" }));
            foreach (var room in new[] { "dojo-courtyard", "dojo" })
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
    public void TheDojo_IsIndoors_SoItHasNoOpenLandscapeAndIsNotOnTheIslandMap()
    {
        var dojo = Content.AllLooks.Single(look => look.Id == "dojo").Look;

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
        foreach (var look in Content.AllLooks.Where(look => look.Look.Wildlife is not null && Content.RoomList.Any(room => room.Id == look.Id)))
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
        var parts = Content.AllLooks.Single(look => look.Id == id).Look.Backdrop.Parts;

        Assert.That(BackdropClearance.Problems(id, layout, parts), Is.Empty);
    }

    [Test]
    public void TheIceRink_IsReachedOnlyFromTheFort()
    {
        var layouts = RoomCatalog.All.ToDictionary(room => room.Id, RoomLayout.Of);
        foreach (var room in Content.RoomList)
        {
            layouts[room.Id] = room.Layout;
        }

        Assert.Multiple(() =>
        {
            Assert.That(Borders(layouts, "fort", RoomEdge.North, "ice-rink"), Is.True, "the rink is placed against the fort's north edge");
            Assert.That(Borders(layouts, "ice-rink", RoomEdge.South, "fort"), Is.True, "and the fort against its south");
            foreach (var room in layouts.Keys.Where(room => room is not ("fort" or "ice-rink")))
            {
                foreach (var edge in new[] { RoomEdge.North, RoomEdge.South, RoomEdge.East, RoomEdge.West })
                {
                    Assert.That(Borders(layouts, "ice-rink", edge, room), Is.False, $"{room} borders the rink");
                }
            }
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
        var facing = rink.Props.Concat(winter.Adds).Where(p => p.Kind is PropKinds.Goal or PropKinds.RinkBoard).Where(p => p.Spot.DistanceTo(new Position(0, 0)) > 5).ToList();

        Assert.That(facing, Is.Not.Empty);
        foreach (var prop in facing)
        {
            // A prop's front is -z turned by its facing, (-sin, -cos); the front must point at the middle of the pitch (the origin).
            var dot = (-Math.Sin(prop.Facing) * -prop.Spot.X) + (-Math.Cos(prop.Facing) * -prop.Spot.Z);
            Assert.That(dot, Is.GreaterThan(0), $"{prop.Kind} at {prop.Spot.X},{prop.Spot.Z} faces away from the pitch");
        }
    }

    [Test]
    public void ThePitch_RunsLeftToRight_WithTheFortDoorOnItsNearTouchline()
    {
        var rink = Content.RoomList.Single(room => room.Id == "ice-rink").Layout;
        var goals = rink.Props.Where(p => p.Kind == PropKinds.Goal).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(rink.Width, Is.GreaterThan(rink.Depth), "landscape");
            Assert.That(rink.Doors, Is.Empty, "the fort is placed against its near touchline");
            Assert.That(goals.Select(g => Math.Sign(g.Spot.X)), Is.EquivalentTo(new[] { -1, 1 }), "a goal at each end");
            Assert.That(goals.All(g => Math.Abs(g.Spot.Z) < 1), Is.True, "in the middle of the end line");
        });
    }

    [Test]
    public void TheFortHasTwoSandcastlesWithFlags_TheForestALittleTreesAndRockCentrepiece_TheCoveAShackAndABonfire()
    {
        var props = Content.RoomList.ToDictionary(room => room.Id, room => room.Layout.Props.Select(p => p.Kind).ToList());
        var designs = Content.PropDesignList.ToDictionary(d => d.Kind);

        Assert.Multiple(() =>
        {
            Assert.That(props["fort"].Count(k => k.StartsWith("field-sandcastle", StringComparison.Ordinal)), Is.EqualTo(2));
            Assert.That(props["forest"], Does.Contain(PropKinds.FieldRock).Or.Contain(PropKinds.FieldBigRock));
            Assert.That(props["forest"].Count(k => k is PropKinds.FieldPine or PropKinds.FieldSapling), Is.GreaterThanOrEqualTo(4), "little trees round the rock");
            Assert.That(props["cove"], Does.Contain(PropKinds.FieldSurfShack).And.Contain(PropKinds.FieldBonfire));
            foreach (var castle in new[] { PropKinds.FieldSandcastle, PropKinds.FieldSandcastleSmall })
            {
                Assert.That(designs[castle].Parts.Any(part => part.Shape == "cylinder" && part.Diameter < 0.2 && part.Height > 0.3), Is.True, $"{castle} has a flag pole");
            }
        });
    }

    [Test]
    public void TheCove_HasWaterOnlyInItsBottomRight()
    {
        var cove = Content.RoomList.Single(room => room.Id == "cove").Layout;
        var painter = Content.PainterList.Single(p => p.Id == Content.AllLooks.Single(look => look.Id == "cove").Look.Floor!.Painter);
        var water = painter.Steps.Where(step => step.Colour == "#5fb3d9").ToList();

        Assert.Multiple(() =>
        {
            Assert.That(cove.Pond!.Center.X, Is.GreaterThan(0));
            Assert.That(cove.Pond.Center.Z, Is.LessThan(0));
            Assert.That(water, Is.Not.Empty);
            Assert.That(water.All(step => step.X > cove.Width / 4 && step.Y > cove.Depth / 6), Is.True, "no water in the top or left of the floor");
        });
    }

    [Test]
    public void EveryFieldPropDesign_IsDrawnInsideItsSize_AndHasAKindOnTheList()
    {
        var fields = Content.PropDesignList.Where(d => d.Kind.StartsWith("field-", StringComparison.Ordinal)).ToList();
        var problems = new List<string>();
        foreach (var design in fields)
        {
            foreach (var part in design.Parts.Where(part => part.Turn is null || part.Turn.All(angle => angle == 0)))
            {
                var reach = part.Shape == "box"
                    ? new[] { -1, 1 }.SelectMany(sx => new[] { -1, 1 }.Select(sz => Math.Sqrt(Math.Pow(part.At[0] + (sx * (part.Width ?? 0) / 2), 2) + Math.Pow(part.At[2] + (sz * (part.Depth ?? 0) / 2), 2)))).Max()
                    : Math.Sqrt((part.At[0] * part.At[0]) + (part.At[2] * part.At[2])) + (Math.Max(part.Diameter ?? 0, part.Top ?? 0) / 2);
                if (reach > design.Size)
                {
                    problems.Add($"{design.Kind}: a {part.Shape} reaches {reach:0.00} (size {design.Size})");
                }
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(fields, Has.Count.GreaterThanOrEqualTo(19));
            Assert.That(fields.Select(d => d.Kind), Is.SubsetOf(PropKinds.All));
            Assert.That(problems, Is.Empty);
        });
    }

    [Test]
    public void ThePropDesignsInTheFile_AreTheSeededOnesWhereBothHaveAKind()
    {
        var starting = PropDesignSeeder.Starting().ToDictionary(d => d.Kind);
        var copies = Content.PropDesignList.Where(d => starting.ContainsKey(d.Kind)).ToList();

        Assert.That(copies.Select(d => d.Kind), Is.SupersetOf(new[] { PropKinds.Goal, PropKinds.IcePatch, PropKinds.RinkBoard, PropKinds.YarnBall, PropKinds.SnowCat }), "the rink's kinds are in both");
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

    [Test]
    public void TheFileIsReadOnce()
    {
        var first = ContentFile.Embedded();
        var second = ContentFile.Embedded();

        Assert.That(second, Is.SameAs(first));
    }

    /// <summary>
    /// A cat walks through scenery: a backdrop part that stands on the floor and is taller than a cat's step must keep off the
    /// walkable floor (apart from a 0.4 m band against the wall, for trim) and off every prop, base and winter alike.
    /// </summary>
    [TestCase("ice-rink")]
    [TestCase("fort")]
    [TestCase("forest")]
    [TestCase("cove")]
    public void TheBackdropOf_StaysClearOfTheWalkableFloorAndTheProps(string id)
    {
        const double wallBand = 0.4;
        var layout = Content.RoomList.Single(room => room.Id == id).Layout;
        var parts = Content.AllLooks.Single(look => look.Id == id).Look.Backdrop.Parts;
        var props = layout.Props.Concat(Content.OverlayList.Where(o => o.RoomId == id).SelectMany(o => o.Adds)).ToList();
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

            foreach (var prop in props)
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

    private static string Json<T>(T value) => System.Text.Json.JsonSerializer.Serialize(value, ContentFile.Json);

    [Test]
    public void EveryVariantFloorInTheFile_NamesAPainterTheBrowserCanDraw()
    {
        var floors = LookNames.BuiltIn.Floors.Concat(Content.PainterList.Where(p => p.Surface == PaintSurfaces.Floor).Select(p => p.Id)).ToHashSet();
        var names = LookNames.BuiltIn.With(Content.PainterList.Select(p => (p.Surface, p.Id)), []);
        var variants = Content.VariantList.Where(v => v.Floor is not null).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(variants, Is.Not.Empty, "the frozen rink has a floor of its own");
            Assert.That(variants.Where(v => !floors.Contains(v.Floor!.Painter)).Select(v => v.Id), Is.Empty);
            Assert.That(variants.SelectMany(v => new LookVariant(v.Sky, v.WallColour, v.Floor).Problems(v.Id, names)), Is.Empty);
        });
    }

    [Test]
    public void Plaza_IsOnTheMap_AndEveryDoorBetweenItsRoomsHasAMatchingOneBack()
    {
        var layouts = Content.RoomList.ToDictionary(room => room.Id, room => room.Layout);
        Assert.Multiple(() =>
        {
            Assert.That(IslandMap.Places.Single(place => place.Id == "plaza").Room, Is.EqualTo("plaza"));
            foreach (var (room, other) in new[] { ("plaza", "pizza-parlor"), ("plaza", "pet-shop"), ("plaza", "hotel"), ("plaza", "mall"), ("town-square", "night-club") })
            {
                var there = layouts[room].Doors.Single(door => door.ToRoom == other);
                var back = layouts[other].Doors.Single(door => door.ToRoom == room);
                Assert.That((there.ToDoor, back.ToDoor), Is.EqualTo((back.Id, there.Id)), $"{room} and {other} name each other's doors");
            }
        });
    }

    /// <summary>Every outdoor border: (room, edge, neighbour, neighbour's edge). The two are placed against each other on the world grid, on the stated edges.</summary>
    private static readonly (string Room, RoomEdge Edge, string Other, RoomEdge OtherEdge)[] OutdoorExits =
    [
        ("plaza", RoomEdge.West, "dock", RoomEdge.East),
        ("plaza", RoomEdge.East, "fort", RoomEdge.West),
        ("town-square", RoomEdge.West, "fort", RoomEdge.East),
        ("town-square", RoomEdge.East, "forest", RoomEdge.West),
        ("fort", RoomEdge.North, "ice-rink", RoomEdge.South),
        ("dock", RoomEdge.North, "peakside-village", RoomEdge.South),
        ("dock", RoomEdge.West, "beach", RoomEdge.East),
        ("beach", RoomEdge.North, "scratching-peaks", RoomEdge.South),
        ("peakside-village", RoomEdge.West, "scratching-peaks", RoomEdge.East),
        ("forest", RoomEdge.East, "cove", RoomEdge.West),
        ("forest", RoomEdge.North, "mine", RoomEdge.South),
        ("mine", RoomEdge.North, "dojo-courtyard", RoomEdge.South),
    ];

    [Test]
    public void EveryOutdoorBorder_IsPlacedAgainstTheNeighbour_OnTheRightEdges_AndNoDoorLeadsFromOneOutdoorRoomToAnother()
    {
        var layouts = LayoutsWithTheDrafts();
        var outdoor = OutdoorExits.SelectMany(e => new[] { e.Room, e.Other }).Distinct().ToList();
        Assert.Multiple(() =>
        {
            foreach (var (room, edge, other, otherEdge) in OutdoorExits)
            {
                Assert.That(Borders(layouts, room, edge, other), Is.True, $"{other} isn't against the {edge} edge of {room}");
                Assert.That(Borders(layouts, other, otherEdge, room), Is.True, $"{room} isn't against the {otherEdge} edge of {other}");
            }
            foreach (var room in outdoor)
            {
                Assert.That(layouts[room].Place, Is.Not.Null, $"{room} has no place on the world grid");
                Assert.That(layouts[room].Doors.Where(door => outdoor.Contains(door.ToRoom)), Is.Empty, $"{room} has a door to another outdoor room: walk there instead");
            }
        });
    }

    [Test]
    public void TheOutdoorRoomsFormOneConnectedWorld_WithNoProblemsAndEveryRoomOnTheMap()
    {
        var layouts = LayoutsWithTheDrafts();
        var places = IslandMap.Places.Where(place => place.Room is not null).Select(place => place.Room!).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(RoomBook.Problems(layouts, []), Is.Empty);
            foreach (var room in OutdoorExits.SelectMany(e => new[] { e.Room, e.Other }).Distinct())
            {
                Assert.That(places, Does.Contain(room), $"{room} is not on the island map");
            }
            Assert.That(layouts.Keys, Does.Not.Contain("garden").And.Not.Contain("pear-orchard").And.Not.Contain("riverside"));
        });
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
    [TestCase("plaza")]
    [TestCase("pizza-parlor")]
    [TestCase("night-club")]
    [TestCase("pet-shop")]
    public void TheBackdropOf_KeepsClearOfTheWalksThePropsAndThePond(string id)
    {
        var layout = Content.RoomList.Single(room => room.Id == id).Layout;
        var parts = Content.AllLooks.Single(look => look.Id == id).Look.Backdrop.Parts;

        Assert.That(BackdropClearance.Problems(id, layout, parts), Is.Empty);
    }

    /// <summary>The Town Square's look is the one a new game starts with (RoomLooks.json), so its shop fronts are checked from there.</summary>
    [Test]
    public void TheTownSquaresStartingBackdrop_HasTheShopFronts_AndKeepsClearOfTheWalksThePropsAndThePond()
    {
        var layout = Content.RoomList.Single(room => room.Id == "town-square").Layout;
        var look = RoomLookSeeder.StartingLooks().Single(l => l.Id == "town-square").Look;

        Assert.Multiple(() =>
        {
            Assert.That(look.Backdrop.Parts.Count, Is.GreaterThanOrEqualTo(150), "the town square's fronts are thin");
            Assert.That(BackdropClearance.Problems("town-square", layout, look.Backdrop.Parts), Is.Empty);
        });
    }

    [TestCase("mine", "town-mine-ground", 120)]
    [TestCase("dojo-courtyard", "town-forecourt", 110)]
    public void TheMineAndTheCourtyard_AreSummerFloorsFromTheirOwnPainters_AndRichlyDecorated(string id, string painter, int parts)
    {
        var look = Content.AllLooks.Single(l => l.Id == id).Look;

        Assert.Multiple(() =>
        {
            Assert.That(look.Floor?.Painter, Is.EqualTo(painter));
            Assert.That(Content.PainterList.Any(p => p.Id == painter && p.Surface == "floor"), Is.True, $"{painter} is not in the file");
            Assert.That(look.Backdrop.Parts.Count, Is.GreaterThanOrEqualTo(parts), $"{id}'s scenery is thin");
        });
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
    public void TheMineShack_IsReachedFromTheDojoCourtyard_AndTheOldTunnelsAreIndoorsBehindItsTunnelDoor()
    {
        var rooms = Content.RoomList.ToDictionary(room => room.Id, room => room.Layout);
        Assert.Multiple(() =>
        {
            Assert.That(rooms.Keys, Does.Contain("mine").And.Contain("mine-tunnels"));
            Assert.That(Borders(rooms, "dojo-courtyard", RoomEdge.South, "mine"), Is.True, "the courtyard is placed against the mine");
            Assert.That(rooms["mine"].Doors.Select(d => (d.Id, d.ToRoom, d.ToDoor)), Is.EqualTo(new[] { ("tunnels", "mine-tunnels", "shack") }));
            Assert.That(rooms["mine-tunnels"].Doors.Select(d => (d.Id, d.ToRoom, d.ToDoor)), Is.EqualTo(new[] { ("shack", "mine", "tunnels") }));
            Assert.That(IslandMap.Places.Select(p => p.Room), Does.Not.Contain("mine-tunnels"), "indoor rooms are not on the island map");
        });
    }

    [Test]
    public void TheMine_BordersTheYardToTheNorthAndTheForestToTheSouth_ItsTunnelDoorIsOnTheFreeEastEdge_NoPropStandsOnADoorMatOrTheSpawn_AndTheLooksAreRich()
    {
        var rooms = Content.RoomList.ToDictionary(room => room.Id, room => room.Layout);
        Assert.Multiple(() =>
        {
            Assert.That(Borders(rooms, "mine", RoomEdge.North, "dojo-courtyard"), Is.True);
            Assert.That(Borders(rooms, "mine", RoomEdge.South, "forest"), Is.True);
            Assert.That(rooms["mine"].Doors.Single().Edge, Is.EqualTo(RoomEdge.East));
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

            Assert.That(Content.AllLooks.Single(look => look.Id == "mine").Look.Backdrop.Parts.Count, Is.GreaterThanOrEqualTo(120), "the mine shack's scenery is thin");
            Assert.That(Content.AllLooks.Single(look => look.Id == "mine-tunnels").Look.Backdrop.Parts.Count, Is.GreaterThanOrEqualTo(120), "the old tunnels' scenery is thin");
        });
    }

    /// <summary>Backdrop parts are not part of the prop-placement checks, so this keeps the mine rooms' decoration off their walks, props and floor flicker (see <see cref="BackdropClearance"/>).</summary>
    [TestCase("mine")]
    [TestCase("mine-tunnels")]
    public void TheBackdropOfTheMineRooms_KeepsClearOfTheWalksAndTheProps(string id)
    {
        var layout = Content.RoomList.Single(room => room.Id == id).Layout;
        var parts = Content.AllLooks.Single(look => look.Id == id).Look.Backdrop.Parts;

        Assert.That(BackdropClearance.Problems(id, layout, parts), Is.Empty);
    }

    /// <summary>The way in and out must be visible and open: nothing stands on a door's mat or where a cat arrives through it, and the old tunnels' north door and the shack's east door are there.</summary>
    [TestCase("mine", "tunnels")]
    [TestCase("mine-tunnels", "shack")]
    public void TheDoorsOfTheMineRooms_AndWhereCatsArriveThroughThem_AreClear(string id, string doorIds)
    {
        const double clearance = 0.4;
        var layout = Content.RoomList.Single(room => room.Id == id).Layout;
        var parts = Content.AllLooks.Single(look => look.Id == id).Look.Backdrop.Parts;
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

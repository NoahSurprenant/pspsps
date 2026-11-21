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
    public void TheBackdropOf_StaysClearOfTheWalkableFloorAndTheProps(string id)
    {
        const double wallBand = 0.4;
        var layout = Content.RoomList.Single(room => room.Id == id).Layout;
        var parts = Content.LookList.Single(look => look.Id == id).Look.Backdrop.Parts;
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

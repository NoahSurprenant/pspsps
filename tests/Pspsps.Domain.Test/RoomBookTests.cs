using Pspsps.Domain.Rooms;

namespace Pspsps.Domain.Test;

public sealed class RoomBookTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    private static Dictionary<string, RoomLayout> Seed() =>
        RoomCatalog.All.ToDictionary(room => room.Id, RoomLayout.Of);

    [Test]
    public void EveryStartingRoom_SurvivesBeingKeptAsALayoutAndBuiltAgain()
    {
        foreach (var room in RoomCatalog.All)
        {
            var again = RoomLayout.Of(room).ToDefinition(room.Id);

            Assert.Multiple(() =>
            {
                Assert.That(again.Doors, Is.EqualTo(room.Doors), room.Id);
                Assert.That(again.Props, Is.EqualTo(room.Props), room.Id);
                Assert.That((again.Name, again.Width, again.Depth, again.Spawn, again.Capacity, again.Pond),
                    Is.EqualTo((room.Name, room.Width, room.Depth, room.Spawn, room.Capacity, room.Pond)), room.Id);
            });
        }
    }

    [Test]
    public void TheStartingRoomsAsABook_AreTheCatalogue()
    {
        var book = RoomBook.Create(Seed(), []);

        Assert.Multiple(() =>
        {
            Assert.That(book.Rooms.Select(room => room.Id), Is.EquivalentTo(RoomCatalog.All.Select(room => room.Id)));
            Assert.That(book.Rooms[0].Id, Is.EqualTo(RoomBook.StartingRoom));
        });
    }

    [Test]
    public void ABook_WithoutTheStartingRoom_HasAProblem()
    {
        var layouts = Seed();
        layouts.Remove(RoomBook.StartingRoom);

        Assert.That(RoomBook.Problems(layouts, []), Is.EqualTo(new[] { "There is no town-square, the room cats arrive in." }));
    }

    [Test]
    public void ADoorToNowhere_OrToADoorThatIsNotThere_IsAProblem()
    {
        var layouts = Seed();
        var cafe = layouts["cafe"];
        layouts["cafe"] = cafe with { Doors = [cafe.Doors[0] with { ToRoom = "attic" }] };
        var shop = layouts[RoomCatalog.ShopId];
        layouts[RoomCatalog.ShopId] = shop with { Doors = [shop.Doors[0] with { ToDoor = "chimney" }] };

        var problems = RoomBook.Problems(layouts, []);

        Assert.That(problems, Is.EqualTo(new[]
        {
            "cafe/town-square: leads to attic, which there is no such room.",
            "shop/town-square: leads to town-square/chimney, which there is no such door.",
        }));
    }

    [Test]
    public void APropOnADoor_IsAProblemAtPublishing()
    {
        var layouts = Seed();
        var shop = layouts["shop"];
        layouts["shop"] = shop with { Props = [.. shop.Props, new Prop(PropKinds.Tree, new Position(-8.1, 3.3), 1)] };

        Assert.That(RoomBook.Problems(layouts, []), Has.Some.Matches<string>(p => p.StartsWith("shop: the tree at", StringComparison.Ordinal)));
    }

    [Test]
    public void AnOverlay_ThatTheLiveRoomCannotHold_WaitsOutOfTheLiveBook()
    {
        var window = new OverlayWindow(Now, Now.AddDays(7));
        var crowded = new RoomOverlay("crowd", "shop", "Crowd", window) { Adds = [new Prop(PropKinds.Tree, new Position(-8.1, 3.3), 1)] };
        var fits = new RoomOverlay("fits", "shop", "Fits", window);
        var layouts = Seed();

        var kept = RoomBook.OverlaysThatFit(layouts, [crowded, fits]);

        Assert.Multiple(() =>
        {
            Assert.That(kept, Is.EqualTo(new[] { fits }));
            Assert.That(() => RoomBook.Create(layouts, kept), Throws.Nothing);
            Assert.That(() => RoomBook.Create(layouts, [crowded, fits]), Throws.TypeOf<DomainException>());
        });
    }

    [Test]
    public void AnOverlay_ThatFits_IsKeptAsItIs()
    {
        var fits = new RoomOverlay("fits", "shop", "Fits", new OverlayWindow(Now, Now.AddDays(7)));

        Assert.That(RoomBook.OverlaysThatFit(Seed(), [fits]), Is.EqualTo(new[] { fits }));
    }

    [Test]
    public void AnOverlayOfARoomThatIsNotThere_OrOneThatBreaksTheRules_IsAProblem()
    {
        var window = new OverlayWindow(Now, Now.AddDays(7));
        var lost = new RoomOverlay("party", "attic", "Party", window);
        var crowded = new RoomOverlay("crowd", "shop", "Crowd", window) { Adds = [new Prop(PropKinds.Tree, new Position(-8.1, 3.3), 1)] };

        var problems = RoomBook.Problems(Seed(), [lost, crowded]);

        Assert.Multiple(() =>
        {
            Assert.That(problems, Has.Some.EqualTo("attic/party: is an overlay of a room that there is not."));
            Assert.That(problems, Has.Some.Matches<string>(p => p.StartsWith("shop/crowd:", StringComparison.Ordinal)));
        });
    }

    [Test]
    public void ARoomsLayout_NeedsAnIdANameASizeAndRoomForCats()
    {
        var layouts = Seed();
        layouts["Bad Id"] = layouts["shop"] with { Name = " ", Width = 0, Capacity = 0, Doors = [] };

        Assert.That(RoomBook.Problems(layouts, []), Is.EqualTo(new[]
        {
            "Bad Id: a room's ID is lower-case letters, digits and hyphens.",
            "Bad Id: has no name.",
            "Bad Id: its floor has no size.",
            "Bad Id: holds no cats.",
        }));
    }

    [Test]
    public void TheBook_GivesARoomWithItsOverlayWhileItIsOn()
    {
        var lamp = new Prop(PropKinds.Lamp, new Position(-10.5, 0), 0.35);
        var overlay = new RoomOverlay("winter", "town-square", "Winter", new OverlayWindow(Now.AddDays(-1), Now.AddDays(30))) { Adds = [lamp], Look = "winter" };
        var book = RoomBook.Create(Seed(), [overlay]);

        Assert.Multiple(() =>
        {
            Assert.That(book.Find("town-square", Now)!.Props, Does.Contain(lamp));
            Assert.That(book.Find("town-square", Now)!.Look, Is.EqualTo("winter"));
            Assert.That(book.Find("town-square", Now.AddDays(60))!.Props, Does.Not.Contain(lamp));
            Assert.That(book.Find("attic", Now), Is.Null);
            Assert.That(book.At(Now).Select(room => room.Id), Is.EqualTo(book.Rooms.Select(room => room.Id)));
        });
    }

    private static (Dictionary<string, RoomLayout> Layouts, RoomLayout Loft) WithALoft(RoomEdge shopEdge, RoomEdge loftEdge)
    {
        var layouts = Seed();
        var shop = layouts["shop"];
        layouts["shop"] = shop with { Props = [], Doors = [.. shop.Doors, new DoorLayout("loft", shopEdge, 0, "loft", "shop")] };
        var loft = new RoomLayout("The Loft", 10, 8, new Position(0, 0), 12) { Doors = [new DoorLayout("shop", loftEdge, 0, "shop", "loft")] };
        return (layouts, loft);
    }

    [Test]
    public void ANewRoom_NeedsDoorsBothWays_SoItIsOnlyFineBesideTheRoomsThatLeadToIt()
    {
        var (layouts, loft) = WithALoft(RoomEdge.West, RoomEdge.East);

        var loftAlone = Seed();
        loftAlone["loft"] = loft;
        var doorsOnly = new Dictionary<string, RoomLayout>(layouts);

        layouts["loft"] = loft;

        Assert.Multiple(() =>
        {
            Assert.That(RoomBook.Problems(layouts, []), Is.Empty, "a set with both ends");
            Assert.That(RoomBook.Problems(loftAlone, []), Is.Not.Empty, "the loft by itself leads to a door that is not there");
            Assert.That(RoomBook.Problems(doorsOnly, []), Is.Not.Empty, "the door by itself leads to a room that is not there");
        });
    }

    [Test]
    public void ABookWithProblems_IsNotBuilt()
    {
        var layouts = Seed();
        layouts.Remove(RoomBook.StartingRoom);

        Assert.That(() => RoomBook.Create(layouts, []), Throws.TypeOf<DomainException>().With.Message.Contain("There is no town-square"));
    }
}

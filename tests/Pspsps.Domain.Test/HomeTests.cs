using Pspsps.Domain.Homes;
using Pspsps.Domain.Rooms;
using Pspsps.Domain.Shop;

namespace Pspsps.Domain.Test;

public sealed class HomeTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Visitor = Guid.NewGuid();

    // A spot well clear of the door, the spawn point and the walks between them.
    private static readonly Position Clear = new(2, -1);

    [Test]
    public void ANewHome_IsEmpty_AndOpenToFriends()
    {
        var home = Home.Create(Owner);

        Assert.Multiple(() =>
        {
            Assert.That(home.OwnerId, Is.EqualTo(Owner));
            Assert.That(home.Furniture, Is.Empty);
            Assert.That(home.Visitors, Is.EqualTo(HomeVisitors.Friends));
        });
    }

    [Test]
    public void TheLayout_HasADoorOutToTownSquare_AndNoPond()
    {
        var door = Home.Layout.GetDoor(Home.DoorOut);

        Assert.Multiple(() =>
        {
            Assert.That(door.ToRoom, Is.EqualTo(RoomCatalog.Default.Id));
            Assert.That(door.ToDoor, Is.Null, "a cat comes out at Town Square's spawn point");
            Assert.That(Home.Layout.Pond, Is.Null);
            Assert.That(door.InReach(Home.Layout.Spawn), Is.False, "a visitor arrives by the door, not on it");
            Assert.That(Home.Layout.Clamp(door.Spot), Is.EqualTo(door.Spot));
            Assert.That(RoomCatalog.Find(Home.Layout.Id), Is.Null, "a home is in no world's catalogue");
        });
    }

    [Test]
    public void EveryPieceOfFurniture_IsDrawnAsAProp_AndFitsInAHome()
    {
        var furniture = ShopCatalog.All.Where(i => i.IsFurniture).ToList();

        Assert.That(furniture, Has.Count.GreaterThanOrEqualTo(8));
        foreach (var item in furniture)
        {
            Assert.That(PropKinds.All, Does.Contain(item.Id), item.Id);
            Assert.That(item.Radius, Is.GreaterThan(0), item.Id);
            var home = Home.Create(Owner);
            Assert.DoesNotThrow(() => home.PlaceAnywhere(item.Id, owned: 1, Now), item.Id);
        }
    }

    [Test]
    public void Place_PutsThePieceWhereAsked_FacingItsWay()
    {
        var home = Home.Create(Owner);

        var piece = home.Place(PropKinds.Sofa, owned: 1, Clear, quarterTurns: 1, Now);

        Assert.Multiple(() =>
        {
            Assert.That(home.Furniture, Is.EqualTo(new[] { piece }));
            Assert.That((piece.ItemId, piece.Spot, piece.QuarterTurns), Is.EqualTo((PropKinds.Sofa, Clear, 1)));
            Assert.That(home.Room("Tom").Props, Is.EqualTo(new[]
            {
                new Prop(PropKinds.Sofa, Clear, ShopCatalog.Find(PropKinds.Sofa)!.Radius, Math.PI / 2),
            }));
            Assert.That(home.Room("Tom").Name, Is.EqualTo("Tom's home"));
        });
    }

    [Test]
    public void Place_WhatTheOwnerHasNone_IsRefused()
    {
        var home = Home.Create(Owner);

        var refused = Assert.Throws<DomainException>(() => home.Place(PropKinds.Sofa, owned: 0, Clear, 0, Now));

        Assert.That(refused!.Message, Is.EqualTo("Buy the Sofa first."));
        Assert.That(home.Furniture, Is.Empty);
    }

    [Test]
    public void Place_AtMostAsManyTimesAsOwned()
    {
        var home = Home.Create(Owner);
        home.Place(PropKinds.Plant, owned: 2, new Position(2, -1), 0, Now);
        home.Place(PropKinds.Plant, owned: 2, new Position(4, -1), 0, Now);

        var third = Assert.Throws<DomainException>(() => home.Place(PropKinds.Plant, owned: 2, new Position(4, -3), 0, Now));
        var anywhere = Assert.Throws<DomainException>(() => home.PlaceAnywhere(PropKinds.Plant, owned: 2, Now));

        Assert.Multiple(() =>
        {
            Assert.That(third!.Message, Is.EqualTo("Every Potted plant you have is out already."));
            Assert.That(anywhere!.Message, Is.EqualTo(third.Message));
            Assert.That(home.Furniture, Has.Count.EqualTo(2));
        });
    }

    [TestCase("party-hat")]
    [TestCase("fur-orange")]
    [TestCase("nothing")]
    public void Place_WhatIsntFurniture_IsRefused(string itemId)
    {
        var refused = Assert.Throws<DomainException>(() => Home.Create(Owner).Place(itemId, owned: 1, Clear, 0, Now));

        Assert.That(refused!.Message, Is.EqualTo("Only furniture goes in a home."));
    }

    [Test]
    public void Place_PartlyOffTheFloor_IsRefused()
    {
        var refused = Assert.Throws<DomainException>(() =>
            Home.Create(Owner).Place(PropKinds.Sofa, owned: 1, new Position(5.5, -1), 0, Now));

        Assert.That(refused!.Message, Is.EqualTo("That won't fit there: it would stick out of the room."));
    }

    [Test]
    public void Place_NowhereOnTheFloor_IsRefused()
    {
        var refused = Assert.Throws<DomainException>(() =>
            Home.Create(Owner).Place(PropKinds.Plant, owned: 1, new Position(double.NaN, 0), 0, Now));

        Assert.That(refused!.Message, Is.EqualTo("That won't fit there: it would stick out of the room."));
    }

    [Test]
    public void Place_OnTheDoorOrTheSpawnPoint_IsRefused()
    {
        var home = Home.Create(Owner);
        var door = Home.Layout.GetDoor(Home.DoorOut).Spot;

        var onDoor = Assert.Throws<DomainException>(() => home.Place(PropKinds.Plant, owned: 1, door with { Z = door.Z - 0.2 }, 0, Now));
        var onSpawn = Assert.Throws<DomainException>(() => home.Place(PropKinds.Plant, owned: 1, Home.Layout.Spawn, 0, Now));

        Assert.Multiple(() =>
        {
            Assert.That(onDoor!.Message, Is.EqualTo("Keep the way to the door clear."));
            Assert.That(onSpawn!.Message, Is.EqualTo("Keep the way to the door clear."));
            Assert.That(home.Furniture, Is.Empty);
        });
    }

    [Test]
    public void Place_OverAnotherPiece_IsRefused_ButBesideItIsFine()
    {
        var home = Home.Create(Owner);
        home.Place(PropKinds.Armchair, owned: 1, new Position(2, -1), 0, Now);

        var over = Assert.Throws<DomainException>(() => home.Place(PropKinds.Plant, owned: 1, new Position(2.8, -1), 0, Now));
        home.Place(PropKinds.Plant, owned: 1, new Position(3.3, -1), 0, Now);

        Assert.Multiple(() =>
        {
            Assert.That(over!.Message, Is.EqualTo("There's something in the way there."));
            Assert.That(home.Furniture.Select(p => p.ItemId), Is.EqualTo(new[] { PropKinds.Armchair, PropKinds.Plant }));
        });
    }

    [TestCase(-1)]
    [TestCase(4)]
    public void Place_TurnedOtherThanAQuarter_IsRefused(int quarterTurns)
    {
        var refused = Assert.Throws<DomainException>(() =>
            Home.Create(Owner).Place(PropKinds.Sofa, owned: 1, Clear, quarterTurns, Now));

        Assert.That(refused!.Message, Is.EqualTo("Furniture turns a quarter at a time."));
    }

    [Test]
    public void AFullHome_TakesNoMore()
    {
        var home = Home.Create(Owner);
        var placed = 0;
        for (var x = -5.5; x <= 5.5 && placed < Home.MaxPieces; x += 0.8)
        {
            for (var z = -4; z <= 4 && placed < Home.MaxPieces; z += 0.8)
            {
                try
                {
                    home.Place(PropKinds.FloorLamp, owned: 100, new Position(x, z), 0, Now);
                    placed++;
                }
                catch (DomainException)
                {
                    // Not there: in the way, or too near another.
                }
            }
        }

        var full = Assert.Throws<DomainException>(() => home.PlaceAnywhere(PropKinds.FloorLamp, owned: 100, Now));

        Assert.That(placed, Is.EqualTo(Home.MaxPieces));
        Assert.That(full!.Message, Is.EqualTo("Your home is full. Put something away first."));
    }

    [Test]
    public void PlaceAnywhere_FindsASpotThatKeepsClear_NearTheMiddle()
    {
        var home = Home.Create(Owner);

        var sofa = home.PlaceAnywhere(PropKinds.Sofa, owned: 1, Now);
        var second = home.PlaceAnywhere(PropKinds.Rug, owned: 1, Now);

        Assert.Multiple(() =>
        {
            Assert.That(PropPlacement.Problems(home.Room("Tom")), Is.Empty);
            Assert.That(sofa.Spot.DistanceTo(new Position(0, 0)), Is.LessThan(2));
            Assert.That(second.Spot, Is.Not.EqualTo(sofa.Spot));
        });
    }

    [Test]
    public void PlaceAnywhere_WithNoRoomLeft_SaysSo()
    {
        var home = Home.Create(Owner);
        for (var i = 0; i < 6; i++)
        {
            home.PlaceAnywhere(PropKinds.Sofa, owned: 100, Now);
        }

        var refused = Assert.Throws<DomainException>(() =>
        {
            for (var i = 0; i < Home.MaxPieces; i++)
            {
                home.PlaceAnywhere(PropKinds.Sofa, owned: 100, Now);
            }
        });

        Assert.That(refused!.Message, Is.EqualTo("There's no room left for the Sofa. Put something away first."));
        Assert.That(PropPlacement.Problems(home.Room("Tom")), Is.Empty, "everything that went in keeps clear");
    }

    [Test]
    public void Turn_GoesAQuarterRound_AndBack()
    {
        var home = Home.Create(Owner);
        var piece = home.Place(PropKinds.Sofa, owned: 1, Clear, 3, Now);

        home.Turn(piece.Id);

        Assert.That(piece.QuarterTurns, Is.Zero);
        Assert.That(home.Room("Tom").Props.Single().Facing, Is.Zero);
    }

    [Test]
    public void PutAway_TakesThePieceOut_AndItCanGoOutAgain()
    {
        var home = Home.Create(Owner);
        var piece = home.Place(PropKinds.Sofa, owned: 1, Clear, 0, Now);

        home.PutAway(piece.Id);
        home.Place(PropKinds.Sofa, owned: 1, new Position(1, -2), 0, Now);

        Assert.That(home.Furniture.Single().Spot, Is.EqualTo(new Position(1, -2)));
    }

    [Test]
    public void TurnOrPutAway_APieceThatIsntThere_IsRefused()
    {
        var home = Home.Create(Owner);

        var turn = Assert.Throws<DomainException>(() => home.Turn(Guid.NewGuid()));
        var putAway = Assert.Throws<DomainException>(() => home.PutAway(Guid.NewGuid()));

        Assert.That(turn!.Message, Is.EqualTo("That isn't in your home any more."));
        Assert.That(putAway!.Message, Is.EqualTo(turn.Message));
    }

    [Test]
    public void EveryChange_CountsAVersion()
    {
        var home = Home.Create(Owner);
        var piece = home.Place(PropKinds.Sofa, owned: 1, Clear, 0, Now);
        home.Turn(piece.Id);
        home.PutAway(piece.Id);
        home.LetIn(HomeVisitors.Anyone);

        Assert.That(home.Version, Is.EqualTo(4));
    }

    [TestCase(HomeVisitors.Friends, false, false)]
    [TestCase(HomeVisitors.Friends, true, true)]
    [TestCase(HomeVisitors.Anyone, false, true)]
    [TestCase(HomeVisitors.Anyone, true, true)]
    [TestCase(HomeVisitors.Nobody, true, false)]
    [TestCase(HomeVisitors.Nobody, false, false)]
    public void MayVisit_IsAsTheOwnerChose(HomeVisitors visitors, bool areFriends, bool may)
    {
        var home = Home.Create(Owner);
        home.LetIn(visitors);

        Assert.That(home.MayVisit(Visitor, areFriends), Is.EqualTo(may));
    }

    [TestCase(HomeVisitors.Friends)]
    [TestCase(HomeVisitors.Anyone)]
    [TestCase(HomeVisitors.Nobody)]
    public void TheOwner_AlwaysMayComeHome(HomeVisitors visitors)
    {
        var home = Home.Create(Owner);
        home.LetIn(visitors);

        Assert.That(home.MayVisit(Owner, areFriends: false), Is.True);
        Assert.DoesNotThrow(() => home.EnsureCanVisit(Owner, areFriends: false));
    }

    [Test]
    public void EnsureCanVisit_SaysWhyNot()
    {
        var home = Home.Create(Owner);
        var notFriends = Assert.Throws<DomainException>(() => home.EnsureCanVisit(Visitor, areFriends: false));
        home.LetIn(HomeVisitors.Nobody);
        var closed = Assert.Throws<DomainException>(() => home.EnsureCanVisit(Visitor, areFriends: true));

        Assert.That(notFriends!.Message, Is.EqualTo("Only friends can visit that home."));
        Assert.That(closed!.Message, Is.EqualTo("That home isn't open to visitors right now."));
    }

    [Test]
    public void LetIn_SomethingThatIsntAChoice_IsRefused()
    {
        var refused = Assert.Throws<DomainException>(() => Home.Create(Owner).LetIn((HomeVisitors)7));

        Assert.That(refused!.Message, Is.EqualTo("That isn't a choice."));
    }

    [Test]
    public void Create_ForNoOne_IsRefused()
    {
        Assert.Throws<DomainException>(() => Home.Create(Guid.Empty));
    }
}

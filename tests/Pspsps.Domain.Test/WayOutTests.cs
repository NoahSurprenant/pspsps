using Pspsps.Domain.Rooms;

namespace Pspsps.Domain.Test;

public sealed class WayOutTests
{
    private static readonly Guid Tom = Guid.NewGuid();
    private static readonly Guid Felix = Guid.NewGuid();

    private static readonly Whereabouts TownSquare = Whereabouts.Room("town-square");
    private static readonly Whereabouts Cafe = Whereabouts.Room("cafe");
    private static readonly Whereabouts Shop = Whereabouts.Room(RoomCatalog.ShopId);


    [Test]
    public void SentOutOfAnotherPlayersHome_TriesTownSquare_ThenTheWorldsOtherRooms_ThenItsOwnHome()
    {
        // The home that sent it out isn't tried: it no longer lets the cat in.
        var places = WayOut.For(RoomCatalog.All, Tom, from: Whereabouts.Home(Felix), refused: null);

        Assert.That(places, Is.EqualTo(new[] { TownSquare, Cafe, Shop, Whereabouts.Home(Tom) }));
    }

    [Test]
    public void RefusedByTownSquare_AfterBeingSentOut_DoesntTryTownSquareAgain()
    {
        var places = WayOut.For(RoomCatalog.All, Tom, from: Whereabouts.Home(Felix), refused: TownSquare);

        Assert.That(places, Is.EqualTo(new[] { Cafe, Shop, Whereabouts.Home(Tom) }));
    }

    [Test]
    public void RefusedByARoom_GoesBackIntoTheRoomItLeftFirst()
    {
        var places = WayOut.For(RoomCatalog.All, Tom, from: Cafe, refused: TownSquare);

        Assert.That(places, Is.EqualTo(new[] { Cafe, Shop, Whereabouts.Home(Tom) }));
    }

    [Test]
    public void RefusedByAHome_FromItsOwnHome_GoesBackHomeFirst()
    {
        var places = WayOut.For(RoomCatalog.All, Tom, from: Whereabouts.Home(Tom), refused: Whereabouts.Home(Felix));

        Assert.That(places, Is.EqualTo(new[] { Whereabouts.Home(Tom), TownSquare, Cafe, Shop, }));
    }

    [Test]
    public void FromNoRoom_EndsInItsOwnHome()
    {
        // Entering a world whose Town Square is full.
        var places = WayOut.For(RoomCatalog.All, Tom, from: null, refused: TownSquare);

        Assert.That(places, Is.EqualTo(new[] { Cafe, Shop, Whereabouts.Home(Tom) }));
    }

    [Test]
    public void EveryWayOut_EndsInTheCatsOwnHome_WhichNeverRefusesIt()
    {
        foreach (var from in new[] { null, TownSquare, Cafe, Whereabouts.Home(Felix) })
        {
            foreach (var refused in new[] { null, TownSquare, Cafe, Shop, Whereabouts.Home(Felix) })
            {
                Assert.That(WayOut.For(RoomCatalog.All, Tom, from, refused), Does.Contain(Whereabouts.Home(Tom)), $"from {from}, refused by {refused}");
            }
        }
    }

    [Test]
    public void Whereabouts_AreEqualByPlace()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Whereabouts.Room("cafe"), Is.EqualTo(Cafe));
            Assert.That(Whereabouts.Home(Tom), Is.EqualTo(Whereabouts.Home(new Guid(Tom.ToString()))));
            Assert.That(Whereabouts.Home(Tom), Is.Not.EqualTo(Whereabouts.Home(Felix)));
        });
    }
}

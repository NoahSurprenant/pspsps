using Pspsps.Domain.Rooms;

namespace Pspsps.Domain.Test;

public sealed class IslandMapTests
{
    [Test]
    public void EveryPlace_HasItsOwnIdAndAKnownCategory()
    {
        var places = IslandMap.Places;
        Assert.Multiple(() =>
        {
            Assert.That(places.Select(p => p.Id), Is.Unique);
            Assert.That(places, Is.Not.Empty);
            foreach (var place in places)
            {
                Assert.That(IslandMap.Categories, Does.Contain(place.Category), place.Id);
                Assert.That(place.Name, Is.Not.Empty, place.Id);
            }
            foreach (var category in IslandMap.Categories)
            {
                Assert.That(places.Count(p => p.Category == category), Is.GreaterThan(0), category);
            }
        });
    }

    [Test]
    public void EveryPlace_StandsOnTheIsland_AndApartFromTheOthers()
    {
        var places = IslandMap.Places;
        Assert.Multiple(() =>
        {
            foreach (var place in places)
            {
                var inside = Math.Pow(place.X / IslandMap.HalfWidth, 2) + Math.Pow(place.Z / IslandMap.HalfDepth, 2);
                Assert.That(inside, Is.LessThan(0.92), $"{place.Id} is too near the shore.");
            }
            for (var i = 0; i < places.Count; i++)
            {
                foreach (var other in places.Skip(i + 1))
                {
                    var apart = Math.Sqrt(Math.Pow(places[i].X - other.X, 2) + Math.Pow(places[i].Z - other.Z, 2));
                    Assert.That(apart, Is.GreaterThanOrEqualTo(12), $"{places[i].Id} is too close to {other.Id}.");
                }
            }
        });
    }

    [Test]
    public void TheStartingRoomsThatAreOutdoors_AreOnTheMap_AndTheTownSquareIsTheFirstPlace()
    {
        var withRooms = IslandMap.Places.Where(p => p.Room is not null).Select(p => p.Room).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(IslandMap.Places[0].Room, Is.EqualTo(RoomBook.StartingRoom));
            Assert.That(withRooms, Is.Unique);
            foreach (var id in new[] { "town-square", "garden", "riverside", "pear-orchard", "mine" })
            {
                Assert.That(withRooms, Does.Contain(id));
            }
            Assert.That(withRooms, Does.Not.Contain("cafe"), "a building's inside is not on the map");
            Assert.That(withRooms, Does.Not.Contain("shop"), "a building's inside is not on the map");
        });
    }
}

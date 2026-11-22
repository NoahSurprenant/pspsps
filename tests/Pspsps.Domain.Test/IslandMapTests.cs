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
            foreach (var id in new[] { "town-square", "mine", "plaza", "fort", "forest", "cove", "ice-rink", "dojo-courtyard", "dock", "beach", "peakside-village", "scratching-peaks" })
            {
                Assert.That(withRooms, Does.Contain(id));
            }
            Assert.That(withRooms, Does.Not.Contain("cafe"), "a building's inside is not on the map");
            Assert.That(withRooms, Does.Not.Contain("shop"), "a building's inside is not on the map");
        });
    }

    /// <summary>Where each place stands on the island picture, as percent across and down it. The map keeps this layout.</summary>
    private static readonly Dictionary<string, (double Across, double Down)> Reference = new()
    {
        ["town-square"] = (69, 61),
        ["plaza"] = (37, 60),
        ["ice-rink"] = (45, 39),
        ["scratching-peaks"] = (19, 26),
        ["mine"] = (83, 46),
        ["cove"] = (96, 68),
        ["lighthouse"] = (7, 63),
        ["harbour-dock"] = (19, 78),
        ["whisker-woods"] = (51, 68),
        ["peakside-village"] = (16, 54),
        ["fort"] = (58, 49),
        ["forest"] = (83, 70),
        ["dojo-courtyard"] = (60, 29),
        ["cat-homes"] = (39, 73),
    };

    /// <summary>The reference picture spans this share of the island's width and depth.</summary>
    private const double Fill = 0.95;

    [Test]
    public void EveryPlace_StandsWhereTheReferenceLayoutPutsIt()
    {
        Assert.Multiple(() =>
        {
            foreach (var place in IslandMap.Places)
            {
                Assert.That(Reference.ContainsKey(place.Id), Is.True, $"{place.Id} has no spot in the reference layout.");
                if (!Reference.TryGetValue(place.Id, out var spot))
                {
                    continue;
                }

                var x = (spot.Across / 100 - 0.5) * 2 * IslandMap.HalfWidth * Fill;
                var z = -(spot.Down / 100 - 0.5) * 2 * IslandMap.HalfDepth * Fill;
                Assert.That(Math.Abs(place.X - x), Is.LessThanOrEqualTo(4), $"{place.Id} is {place.X} east, the reference says {x:0.#}.");
                Assert.That(Math.Abs(place.Z - z), Is.LessThanOrEqualTo(4), $"{place.Id} is {place.Z} north, the reference says {z:0.#}.");
            }
            foreach (var id in Reference.Keys)
            {
                Assert.That(IslandMap.Places.Select(p => p.Id), Does.Contain(id), $"{id} is in the reference layout but not on the map.");
            }
        });
    }
}

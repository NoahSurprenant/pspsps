using Pspsps.Domain.Rooms;
using Pspsps.Domain.Shop;

namespace Pspsps.Domain.Homes;

/// <summary>
/// A piece of furniture standing in a home: which shop item it is, where (its middle, on the home's floor), and which
/// way it faces, in quarter turns. Part of the <see cref="Home"/> aggregate.
/// </summary>
public sealed class PlacedFurniture
{
    private PlacedFurniture()
    {
        ItemId = null!;
    }

    internal PlacedFurniture(Guid id, string itemId, Position spot, int quarterTurns)
    {
        Id = id;
        ItemId = itemId;
        X = spot.X;
        Z = spot.Z;
        QuarterTurns = quarterTurns;
    }

    public Guid Id { get; private set; }

    /// <summary>The shop item (<see cref="ShopCatalog"/>), whose ID is also the kind of prop it's drawn as.</summary>
    public string ItemId { get; private set; }

    public double X { get; private set; }

    public double Z { get; private set; }

    /// <summary>Which way it faces: 0 to the front of the room, then a quarter turn more for each (0 to 3).</summary>
    public int QuarterTurns { get; private set; }

    /// <summary>Where it stands.</summary>
    public Position Spot => new(X, Z);

    /// <summary>The piece as the room's decoration: its kind, spot, size and facing.</summary>
    public Prop AsProp() => Home.PropOf(ItemId, Spot, QuarterTurns);

    internal void Turn() => QuarterTurns = (QuarterTurns + 1) % Home.Turns;
}

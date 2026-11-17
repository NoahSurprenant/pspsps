using Pspsps.Domain.Homes;

namespace Pspsps.Application.Homes;

/// <summary>
/// A player's home as it is: whose it is (their name, for the room's), who may visit, its furniture, and its version
/// (which of two plans is newer).
/// </summary>
public sealed record HomePlan(
    Guid OwnerId, string OwnerName, HomeVisitors Visitors, IReadOnlyList<PlacedFurniture> Furniture, int Version = 0)
{
    public static HomePlan Of(Home home, string ownerName)
    {
        ArgumentNullException.ThrowIfNull(home);
        return new HomePlan(home.OwnerId, ownerName, home.Visitors, [.. home.Furniture], home.Version);
    }
}

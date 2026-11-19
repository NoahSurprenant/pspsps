namespace Pspsps.Domain.Rooms;

/// <summary>
/// Where a cat goes when it has to be somewhere and the room it was going to didn't take it: sent out of a home (whose
/// owner no longer lets it stay), refused by a full room past a door, by a home that wouldn't open, or by a full Town
/// Square on entering a world. A cat is never left in no room: the last place to try is its own home, which always has
/// room for its owner (<see cref="RoomState"/>).
/// </summary>
public static class WayOut
{
    /// <summary>
    /// The places to try, in order, for <paramref name="playerId"/>'s cat, which came from <paramref name="from"/> (null
    /// when it was in no room) and was just refused by <paramref name="refused"/> (null when it wasn't): back into the
    /// room it came from, unless that is another player's home (which may no longer let it in); then the world's rooms in
    /// the catalogue's order (Town Square, the café, the shop); then its own home. The room that just refused it isn't
    /// tried again, and no place twice.
    /// </summary>
    public static IReadOnlyList<Whereabouts> For(Guid playerId, Whereabouts? from, Whereabouts? refused)
    {
        var places = new List<Whereabouts>();
        if (from is not null && (from.HomeOwner is null || from.HomeOwner == playerId))
        {
            places.Add(from);
        }

        places.AddRange(RoomCatalog.All.Select(room => Whereabouts.Room(room.Id)));
        places.Add(Whereabouts.Home(playerId));
        return [.. places.Distinct().Where(place => place != refused)];
    }
}

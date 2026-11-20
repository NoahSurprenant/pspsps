using Pspsps.Domain.Rooms;

namespace Pspsps.Application.Rooms;

/// <summary>
/// The world's public rooms as players have them now: each room's published version, with the overlays that are on at
/// this moment laid over it. Kept in memory on every pod and read again from the database every few seconds
/// (<see cref="RefreshAsync"/>), so a room published on one pod reaches the others without a deploy.
/// </summary>
public interface IRoomLibrary
{
    /// <summary>The room as it is now; null when there is no such room.</summary>
    RoomDefinition? Find(string id);

    /// <summary>Every room as it is now, the starting room first.</summary>
    IReadOnlyList<RoomDefinition> All { get; }

    /// <summary>The room cats arrive in.</summary>
    RoomDefinition Start { get; }

    /// <summary>Reads the published rooms and the overlays again. When they can't be read or are not sound, the library keeps what it has.</summary>
    Task RefreshAsync(CancellationToken cancellationToken);
}

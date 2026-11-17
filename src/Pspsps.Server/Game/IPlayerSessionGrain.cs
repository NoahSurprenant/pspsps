using Orleans.Concurrency;

namespace Pspsps.Server.Game;

/// <summary>
/// Which connection (browser tab) is a player's current one, keyed by player ID, and which room its cat is in: where
/// the player's friends find them. A player plays in one tab: the newest.
/// </summary>
public interface IPlayerSessionGrain : IGrainWithGuidKey
{
    /// <summary>Makes <paramref name="connectionId"/> the current tab; returns the tab it replaced, if any.</summary>
    Task<string?> Claim(string connectionId);

    /// <summary>
    /// Whether <paramref name="connectionId"/> is the current tab. With none recorded (this grain was collected while
    /// the tab sat idle), the asking tab becomes current.
    /// </summary>
    Task<bool> IsCurrent(string connectionId);

    /// <summary>Forgets <paramref name="connectionId"/> if it's the current tab (it closed).</summary>
    Task Release(string connectionId);

    /// <summary>
    /// Records that the cat of <paramref name="connectionId"/>, the current tab, is now in the room
    /// <paramref name="roomKey"/> (<see cref="RoomKey"/>). A tab that isn't current is ignored.
    /// </summary>
    Task Arrived(string connectionId, string roomKey);

    /// <summary>
    /// Where the player's cat is, as its room confirms: null when the player is offline, between rooms, or the room no
    /// longer has their cat (its lease ran out). Interleaves: the player's own calls (IsCurrent on every move) never
    /// wait behind friends asking where they are.
    /// </summary>
    [AlwaysInterleave]
    Task<PlayerPlace?> Locate();
}

namespace Pspsps.Server.Game;

/// <summary>
/// Which connection (browser tab) is a player's current one, keyed by player ID. A player plays in one tab: the newest.
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
}

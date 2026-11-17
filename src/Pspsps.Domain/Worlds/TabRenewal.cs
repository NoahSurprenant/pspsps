namespace Pspsps.Domain.Worlds;

/// <summary>What renewing a session's (a tab's) lease on its place in a world found.</summary>
public enum TabRenewal
{
    /// <summary>The world counts the tab; its lease starts again.</summary>
    Renewed,

    /// <summary>The world had forgotten the tab (it came back empty after its pod died) and counts it in again.</summary>
    CountedBackIn,

    /// <summary>The tab left this world lately; a renewal sent before it left doesn't bring it back.</summary>
    Left,

    /// <summary>The world had forgotten the tab and is full now: the tab has lost its place.</summary>
    Full,
}

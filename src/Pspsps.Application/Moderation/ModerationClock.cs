namespace Pspsps.Application.Moderation;

/// <summary>
/// The clock bans and mutes are timed by: a keyed <see cref="TimeProvider"/> (the system's), which tests swap for one
/// they move, so a mute can be seen to end at its time.
/// </summary>
public static class ModerationClock
{
    public const string Key = "pspsps:moderation";
}

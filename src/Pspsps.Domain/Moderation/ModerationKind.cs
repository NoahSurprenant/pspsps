namespace Pspsps.Domain.Moderation;

/// <summary>What an admin did to a player, in the audit trail (<see cref="ModerationEntry"/>).</summary>
public enum ModerationKind
{
    Warned,
    Muted,
    Banned,
    MuteLifted,
    BanLifted,
}

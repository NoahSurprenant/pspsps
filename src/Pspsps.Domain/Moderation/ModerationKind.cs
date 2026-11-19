namespace Pspsps.Domain.Moderation;

/// <summary>What an admin did to a player, in the audit trail (<see cref="ModerationEntry"/>).</summary>
public enum ModerationKind
{
    Warned,
    Muted,
    Banned,
    MuteLifted,
    BanLifted,

    /// <summary>A network the player played from was banned with them (<see cref="AddressBan"/>).</summary>
    AddressBanned,

    /// <summary>A network's ban was lifted.</summary>
    AddressBanLifted,

    /// <summary>An admin renamed the cat from its card, to fix a rude name.</summary>
    Renamed,
}

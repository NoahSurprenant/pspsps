namespace Pspsps.Application.Moderation;

/// <summary>Admins' moderation of players: warn, mute, ban, lift, and the players' standing.</summary>
public interface IModerationService
{
    /// <summary>Whether the player is an admin (configured by player ID).</summary>
    bool IsAdmin(Guid playerId);

    /// <summary>The player's ban and mute in force now.</summary>
    Task<Standing> StandingAsync(Guid playerId, CancellationToken cancellationToken);

    /// <summary>
    /// <paramref name="adminId"/> acts on <paramref name="playerId"/> (the rules: <see cref="Domain.Moderation.PlayerModeration"/>);
    /// the player's standing after.
    /// </summary>
    Task<Standing> ModerateAsync(
        Guid adminId, Guid playerId, ModerationRequest request, CancellationToken cancellationToken);

    /// <summary>The warnings the player hasn't seen yet, oldest first: shown until they have (<see cref="SawWarningAsync"/>).</summary>
    Task<IReadOnlyList<PendingWarning>> WarningsAsync(Guid playerId, CancellationToken cancellationToken);

    /// <summary>The player has seen the warning <paramref name="warningId"/> (and those before it).</summary>
    Task SawWarningAsync(Guid playerId, Guid warningId, CancellationToken cancellationToken);

    /// <summary>For an admin: who is banned or muted now, and the latest lines of the audit trail.</summary>
    Task<ModerationOverview> OverviewAsync(Guid adminId, CancellationToken cancellationToken);
}

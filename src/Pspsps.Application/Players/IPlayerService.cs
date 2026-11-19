using Pspsps.Domain.Players;

namespace Pspsps.Application.Players;

public interface IPlayerService
{
    /// <summary>The player behind an external login, created on its first sign-in.</summary>
    Task<PlayerProfile> SignInWithLoginAsync(
        LoginProvider provider, string providerKey, string? profileName, CancellationToken cancellationToken);

    /// <summary>
    /// Adds an external login to an existing player ("Save your cat" for a guest), keeping their cat and coins. A login
    /// that already belongs to another player is refused, not merged.
    /// </summary>
    Task<LinkLoginOutcome> LinkLoginAsync(
        Guid playerId, LoginProvider provider, string providerKey, CancellationToken cancellationToken);

    /// <summary>A new guest player with a generated name.</summary>
    Task<PlayerProfile> CreateGuestAsync(CancellationToken cancellationToken);

    Task<PlayerProfile?> FindAsync(Guid playerId, CancellationToken cancellationToken);

    /// <summary>The ID of the player behind an external login, if it has one yet (none is made).</summary>
    Task<Guid?> FindByLoginAsync(LoginProvider provider, string providerKey, CancellationToken cancellationToken);

    /// <summary>Pays the player <paramref name="coins"/> (a catch, a game's prize); the player as saved after.</summary>
    Task<PlayerProfile> EarnCoinsAsync(Guid playerId, int coins, CancellationToken cancellationToken);

    /// <summary>
    /// Renames the player's own cat, from their own card (<see cref="Domain.Players.Player.Rename"/>): the name rules, the
    /// chat filter's lists, no staff impersonation, and at most once a day, unless the player is an admin (configured),
    /// who is exempt. The player as saved after.
    /// </summary>
    Task<PlayerProfile> RenameAsync(Guid playerId, string displayName, CancellationToken cancellationToken);
}

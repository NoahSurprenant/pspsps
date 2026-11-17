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
}

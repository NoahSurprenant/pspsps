using Pspsps.Domain.Players;

namespace Pspsps.Application.Players;

public interface IPlayerService
{
    /// <summary>The player behind an external login, created on its first sign-in.</summary>
    Task<PlayerProfile> SignInWithLoginAsync(
        LoginProvider provider, string providerKey, string? profileName, CancellationToken cancellationToken);

    /// <summary>A new guest player with a generated name.</summary>
    Task<PlayerProfile> CreateGuestAsync(CancellationToken cancellationToken);

    Task<PlayerProfile?> FindAsync(Guid playerId, CancellationToken cancellationToken);
}

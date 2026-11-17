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

    /// <summary>Pays the player <paramref name="coins"/> (a catch, a game's prize); the player as saved after.</summary>
    Task<PlayerProfile> EarnCoinsAsync(Guid playerId, int coins, CancellationToken cancellationToken);
}

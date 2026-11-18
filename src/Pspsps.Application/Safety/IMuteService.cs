namespace Pspsps.Application.Safety;

/// <summary>
/// A player's mutes: whose chat their browser hides. Every method is from <c>playerId</c>'s side and returns their mutes
/// as they are after, by name; a rule broken (muting yourself, twice, too many) is a <see cref="Domain.DomainException"/>.
/// </summary>
public interface IMuteService
{
    /// <summary>The players this one has muted, by name.</summary>
    Task<IReadOnlyList<MutedPlayer>> ListAsync(Guid playerId, CancellationToken cancellationToken);

    /// <summary>Mutes the other player.</summary>
    Task<IReadOnlyList<MutedPlayer>> MuteAsync(Guid playerId, Guid otherId, CancellationToken cancellationToken);

    /// <summary>Unmutes the other player.</summary>
    Task<IReadOnlyList<MutedPlayer>> UnmuteAsync(Guid playerId, Guid otherId, CancellationToken cancellationToken);
}

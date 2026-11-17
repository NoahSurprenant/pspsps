using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Pspsps.Domain.Players;

namespace Pspsps.Integration.Test;

/// <summary>
/// Runs a test's action in the server just before it saves a change to one player, once: to see what the rest of the
/// game had done by then, or to make something happen at exactly that moment.
/// </summary>
public sealed class BeforeSaving : SaveChangesInterceptor
{
    private readonly ConcurrentDictionary<Guid, Func<Task>> _actions = new();

    /// <summary>Runs <paramref name="action"/> before the next save that changes <paramref name="playerId"/>.</summary>
    public void Once(Guid playerId, Func<Task> action) => _actions[playerId] = action;

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        var changed = eventData.Context?.ChangeTracker.Entries<Player>()
            .Where(e => e.State == EntityState.Modified)
            .Select(e => e.Entity.Id)
            .ToList() ?? [];
        foreach (var playerId in changed)
        {
            if (_actions.TryRemove(playerId, out var action))
            {
                await action();
            }
        }

        return result;
    }
}

using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Pspsps.Domain.Friends;
using Pspsps.Domain.Players;

namespace Pspsps.Integration.Test;

/// <summary>
/// Runs a test's action in the server just before it writes a change to one player (a save, or an update statement
/// such as ExecuteUpdate's) or to a friendship of theirs, once: to see what the rest of the game had done by then, or to
/// make something happen at exactly that moment.
/// </summary>
public sealed class BeforeSaving : SaveChangesInterceptor
{
    private readonly ConcurrentDictionary<Guid, Func<Task>> _actions = new();

    public BeforeSaving()
    {
        Statements = new UpdateStatements(this);
    }

    /// <summary>The part that sees update statements: register it with the context too.</summary>
    public DbCommandInterceptor Statements { get; }

    /// <summary>Runs <paramref name="action"/> before the next write that changes <paramref name="playerId"/>.</summary>
    public void Once(Guid playerId, Func<Task> action) => _actions[playerId] = action;

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        var changed = eventData.Context?.ChangeTracker.Entries<Player>()
            .Where(e => e.State == EntityState.Modified)
            .Select(e => e.Entity.Id)
            .Concat(eventData.Context?.ChangeTracker.Entries<Friendship>()
                .Where(e => e.State is EntityState.Modified or EntityState.Deleted)
                .SelectMany(e => new[] { e.Entity.FirstPlayerId, e.Entity.SecondPlayerId }) ?? [])
            .ToList() ?? [];
        await RunFor(changed);
        return result;
    }

    private async Task RunFor(IEnumerable<Guid> playerIds)
    {
        foreach (var playerId in playerIds)
        {
            if (_actions.TryRemove(playerId, out var action))
            {
                await action();
            }
        }
    }

    private sealed class UpdateStatements(BeforeSaving owner) : DbCommandInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(command);
            if (command.CommandText.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase))
            {
                await owner.RunFor(command.Parameters.Cast<DbParameter>().Select(p => p.Value).OfType<Guid>().ToList());
            }

            return result;
        }
    }
}

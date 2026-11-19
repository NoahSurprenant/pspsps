using Microsoft.EntityFrameworkCore;
using Pspsps.Application.Abstractions;
using Pspsps.Domain;

namespace Pspsps.Application.Moderation;

/// <summary>
/// How moderation changes are saved (players' moderation, reports, networks' bans): decided on what was read, tracked,
/// then saved in one go, and decided again when another change landed on the same rows meanwhile.
/// </summary>
internal static class ModerationSaves
{
    /// <summary>How many times a change is decided again when another landed on the same rows meanwhile.</summary>
    public const int Attempts = 5;

    /// <summary>
    /// One try at a change: what to do just before its save (add the new audit lines), and how to forget what it read if
    /// the save is refused.
    /// </summary>
    public sealed class Attempt
    {
        public List<Action> BeforeSave { get; } = [];

        public List<Action> Forget { get; } = [];
    }

    /// <summary>
    /// Decides a change (<paramref name="decide"/>: reads what it needs, tracked, and changes it by the Domain's rules), then
    /// saves it all at once. Concurrency tokens (a moderation's or a network ban's Version, a report's Status) refuse a
    /// save when another change landed between the read and the save (two admins at once, a warning seen as another is
    /// given, a report closed meanwhile); two firsts at once collide on a key. Either way the change is decided again on
    /// what is there now, which may refuse it. A rule broken (a DomainException) saves nothing.
    /// </summary>
    public static async Task<T> SaveAsync<T>(
        IApplicationDbContext db, Func<Attempt, Task<T>> decide, CancellationToken cancellationToken)
    {
        for (var tries = 1; ; tries++)
        {
            var attempt = new Attempt();
            try
            {
                var result = await decide(attempt);
                attempt.BeforeSave.ForEach(step => step());

                await db.SaveChangesAsync(cancellationToken);
                return result;
            }
            catch (DomainException)
            {
                attempt.Forget.ForEach(step => step());
                throw;
            }
            catch (DbUpdateException saving) when (saving is DbUpdateConcurrencyException || db.IsUniqueViolation(saving))
            {
                attempt.Forget.ForEach(step => step());
                if (tries == Attempts)
                {
                    throw new DomainException("That changed just now. Try again.");
                }
            }
        }
    }
}

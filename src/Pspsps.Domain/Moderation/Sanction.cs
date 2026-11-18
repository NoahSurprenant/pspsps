using System.Globalization;

namespace Pspsps.Domain.Moderation;

/// <summary>
/// A ban or a mute: why (shown to the player), since when, until when (null: for good, which only a ban can be) and
/// who decided it.
/// </summary>
public sealed record Sanction(string Reason, DateTimeOffset Since, DateTimeOffset? Until, Guid By)
{
    /// <summary>A ban with no end: it lasts until an admin lifts it.</summary>
    public bool IsPermanent => Until is null;

    /// <summary>Whether it still holds at <paramref name="now"/>: it ends at <see cref="Until"/>, not a moment later.</summary>
    public bool InForceAt(DateTimeOffset now) => Until is not { } until || now < until;

    /// <summary>"until 18 Nov 2026 15:04 UTC" or "for good": the end, as the server says it (the browser shows local time).</summary>
    public string Ends() => Until is { } until
        ? "until " + until.ToUniversalTime().ToString("d MMM yyyy HH:mm 'UTC'", CultureInfo.InvariantCulture)
        : "for good";

    /// <summary>What a banned player's refused calls say.</summary>
    public string SaysBanned() => $"Your cat is banned {Ends()}. Why: {Reason}";

    /// <summary>What a muted player's refused chat says.</summary>
    public string SaysMuted() => $"You're muted {Ends()}. Why: {Reason}";
}

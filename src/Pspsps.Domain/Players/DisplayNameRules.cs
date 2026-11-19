using System.Text;
using Pspsps.Domain.Rooms;

namespace Pspsps.Domain.Players;

/// <summary>The rules for the name other cats see above a player's head.</summary>
public static class DisplayNameRules
{
    public const int MinLength = 3;
    public const int MaxLength = 20;

    /// <summary>What a name the chat filter would mask or refuse is told.</summary>
    public const string FilteredMessage = "Pick another name: that one isn't allowed.";

    /// <summary>What a name impersonating staff is told.</summary>
    public const string ImpersonatingMessage = "Pick another name: that one sounds like an admin or moderator.";

    // A small, fixed list: never on a player's own chosen name, whatever the configured chat filter has. Checked on the
    // normalized name (trimmed, collapsed spaces), case-insensitively; "mod " (with the trailing space) catches "mod Joe"
    // without also catching "modern".
    private static readonly string[] StaffWords = ["admin", "moderator", "mod "];

    /// <summary>Trims the name and collapses runs of spaces, or refuses it.</summary>
    public static string Normalize(string? name)
    {
        var collapsed = string.Join(' ', (name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (collapsed.Length is < MinLength or > MaxLength)
        {
            throw new DomainException($"A name is {MinLength} to {MaxLength} characters long.");
        }

        if (!collapsed.All(IsAllowed))
        {
            throw new DomainException("A name has only letters, digits, spaces, '-' and '_'.");
        }

        return collapsed;
    }

    /// <summary>
    /// Refuses <paramref name="name"/> (already <see cref="Normalize"/>d) when <paramref name="chatFilter"/> would mask or
    /// refuse it, or when it impersonates staff (a small fixed list: "admin", "moderator", "mod ", case-insensitive).
    /// </summary>
    public static void EnsureAllowed(string name, ChatFilter chatFilter)
    {
        ArgumentNullException.ThrowIfNull(chatFilter);
        string applied;
        try
        {
            applied = chatFilter.Apply(name);
        }
        catch (DomainException)
        {
            throw new DomainException(FilteredMessage);
        }

        // A mask list stars a word out, with nothing to say: a starred name is never allowed.
        if (!string.Equals(applied, name, StringComparison.Ordinal))
        {
            throw new DomainException(FilteredMessage);
        }

        if (StaffWords.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainException(ImpersonatingMessage);
        }
    }

    /// <summary>
    /// Makes a valid name out of anything (a Discord or Google profile name), falling back to <paramref name="fallback"/>.
    /// </summary>
    public static string FromProfile(string? profileName, string fallback)
    {
        var kept = new StringBuilder();
        foreach (var c in profileName ?? "")
        {
            if (kept.Length == MaxLength)
            {
                break;
            }

            if (IsAllowed(c))
            {
                kept.Append(c);
            }
        }

        try
        {
            return Normalize(kept.ToString());
        }
        catch (DomainException)
        {
            return Normalize(fallback);
        }
    }

    private static bool IsAllowed(char c) => char.IsAsciiLetterOrDigit(c) || c is ' ' or '-' or '_';
}

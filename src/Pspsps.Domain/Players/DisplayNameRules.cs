using System.Text;

namespace Pspsps.Domain.Players;

/// <summary>The rules for the name other cats see above a player's head.</summary>
public static class DisplayNameRules
{
    public const int MinLength = 3;
    public const int MaxLength = 20;

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

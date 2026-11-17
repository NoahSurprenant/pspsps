namespace Pspsps.Domain.Rooms;

/// <summary>What a word list does to a chat line with one of its words in it.</summary>
public enum ChatFilterAction
{
    /// <summary>The line is said with the word starred out.</summary>
    Mask,

    /// <summary>The line isn't said at all; the player is told why.</summary>
    Refuse,
}

/// <summary>Words that do one <see cref="ChatFilterAction"/>.</summary>
public sealed record ChatWordList(ChatFilterAction Action, IReadOnlyList<string> Words);

/// <summary>
/// The words a room's chat won't carry. A word is found as a whole word, in any case, and through the usual ways of
/// hiding it: spaces or punctuation between its letters ("b a d", "b.a.d"), digits and symbols for letters ("b4d"),
/// and stretched letters ("baaad"). It isn't found inside a longer word, so "class" doesn't trip "ass".
/// </summary>
public sealed class ChatFilter
{
    public const string RefusedMessage = "Keep it friendly: that message has a word this game doesn't allow.";

    // Characters people put in for letters; each stands for any of the letters after it.
    private static readonly Dictionary<char, string> LookAlikes = new()
    {
        ['0'] = "o",
        ['1'] = "il",
        ['3'] = "e",
        ['4'] = "a",
        ['5'] = "s",
        ['7'] = "t",
        ['8'] = "b",
        ['9'] = "g",
        ['@'] = "a",
        ['$'] = "s",
        ['!'] = "i",
        ['|'] = "il",
        ['+'] = "t",
    };

    private readonly string[] _masked;
    private readonly string[] _refused;

    private ChatFilter(string[] masked, string[] refused)
    {
        _masked = masked;
        _refused = refused;
    }

    /// <summary>A filter that lets everything through.</summary>
    public static ChatFilter None { get; } = new([], []);

    public static ChatFilter Create(IEnumerable<ChatWordList> lists)
    {
        ArgumentNullException.ThrowIfNull(lists);
        var masked = new HashSet<string>();
        var refused = new HashSet<string>();
        foreach (var list in lists)
        {
            var words = list.Action == ChatFilterAction.Refuse ? refused : masked;
            words.UnionWith(list.Words.Select(Normalize).Where(word => word.Length > 0));
        }

        return new ChatFilter([.. masked.Except(refused)], [.. refused]);
    }

    /// <summary>
    /// The line as the room hears it: words from a mask list starred out. Refuses the line when it has a word from a
    /// refuse list.
    /// </summary>
    public string Apply(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (_refused.Any(word => Find(text, word, 0) is not null))
        {
            throw new DomainException(RefusedMessage);
        }

        var masked = text.ToCharArray();
        foreach (var word in _masked)
        {
            var from = 0;
            while (Find(text, word, from) is { } found)
            {
                for (var i = found.Start; i < found.End; i++)
                {
                    if (!char.IsWhiteSpace(masked[i]))
                    {
                        masked[i] = '*';
                    }
                }

                from = found.End;
            }
        }

        return new string(masked);
    }

    // A listed word as the letters it is made of: lower case, look-alikes read as their first letter, the rest dropped.
    private static string Normalize(string? word) =>
        string.Concat((word ?? "")
            .Select(c => char.IsLetter(c) ? char.ToLowerInvariant(c) : LookAlikes.TryGetValue(c, out var letters) ? letters[0] : '\0')
            .Where(c => c != '\0'));

    // The first place at or after `from` where `word` is written as a whole word; null when nowhere.
    private static (int Start, int End)? Find(string text, string word, int from)
    {
        for (var start = from; start < text.Length; start++)
        {
            if ((start == 0 || !char.IsLetterOrDigit(text[start - 1])) && Stands(text[start], word[0]))
            {
                var dead = new HashSet<(int, int)>();
                if (MatchFrom(text, word, start + 1, 1, dead) is int end)
                {
                    return (start, end);
                }
            }
        }

        return null;
    }

    // Matches the rest of `word` from letter `j` at text position `i`, returning where the match ends. `dead` remembers
    // the positions already tried, so a long line of repeated letters can't make this slow.
    private static int? MatchFrom(string text, string word, int i, int j, HashSet<(int, int)> dead)
    {
        if (j == word.Length && (i == text.Length || !char.IsLetterOrDigit(text[i])))
        {
            return i;
        }

        if (i == text.Length || !dead.Add((i, j)))
        {
            return null;
        }

        var c = text[i];
        if (IsLetterLike(c))
        {
            // The next letter of the word, or the last one again ("baaad").
            return (j < word.Length && Stands(c, word[j]) ? MatchFrom(text, word, i + 1, j + 1, dead) : null)
                ?? (Stands(c, word[j - 1]) ? MatchFrom(text, word, i + 1, j, dead) : null);
        }

        // Spaces and punctuation between the word's letters ("b a d", "b.a.d").
        return j < word.Length ? MatchFrom(text, word, i + 1, j, dead) : null;
    }

    private static bool IsLetterLike(char c) => char.IsLetterOrDigit(c) || LookAlikes.ContainsKey(c);

    // Whether the character in the text can stand for the word's letter.
    private static bool Stands(char c, char letter) =>
        char.ToLowerInvariant(c) == letter || (LookAlikes.TryGetValue(c, out var letters) && letters.Contains(letter, StringComparison.Ordinal));
}

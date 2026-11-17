using System.Collections.Frozen;
using System.Globalization;
using System.Text;

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
/// hiding it: accents and look-alike letters from other alphabets ("dárn"), digits and symbols for letters ("b4d"),
/// stretched letters ("baaad"), and single letters spaced or dotted out ("b a d", "b.a.d"). It isn't found inside a
/// longer word ("class" doesn't trip "ass"), nor spelled across real words ("it's hit" doesn't spell "shit").
/// </summary>
/// <remarks>
/// A listed word made only of letters and look-alikes is matched that way. One with any other character, with no letter
/// at all, or of a single letter ("69", "2g1c") is matched only as written, so it never widens to other words.
/// </remarks>
public sealed class ChatFilter
{
    public const string RefusedMessage = "Keep it friendly: that message has a word this game doesn't allow.";

    // Lines and words up to this long are checked on the stack; RoomState.MaxChatLength is well inside it.
    private const int StackLimit = 256;

    // Characters people put in for letters; each stands for any of the letters after it.
    private static readonly FrozenDictionary<char, string> LookAlikes = new Dictionary<char, string>
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
    }.ToFrozenDictionary();

    // Lower-case letters read as the Latin letter they look like: accented Latin letters, and the Cyrillic and Greek
    // letters that pass for Latin ones.
    private static readonly FrozenDictionary<char, char> Folded = BuildFolded();

    // Words matched through look-alikes, by their first letter; words matched only as written. Word → refused.
    private readonly FrozenDictionary<char, Entry[]> _byFirstLetter;
    private readonly Dictionary<string, bool> _asWritten;
    private readonly Dictionary<string, bool>.AlternateLookup<ReadOnlySpan<char>> _asWrittenLookup;

    private ChatFilter(FrozenDictionary<char, Entry[]> byFirstLetter, Dictionary<string, bool> asWritten)
    {
        _byFirstLetter = byFirstLetter;
        _asWritten = asWritten;
        _asWrittenLookup = asWritten.GetAlternateLookup<ReadOnlySpan<char>>();
    }

    /// <summary>A filter that lets everything through.</summary>
    public static ChatFilter None { get; } = Create([]);

    /// <summary>Whether no word is listed, so nothing is filtered.</summary>
    public bool IsEmpty => _byFirstLetter.Count == 0 && _asWritten.Count == 0;

    public static ChatFilter Create(IEnumerable<ChatWordList> lists)
    {
        ArgumentNullException.ThrowIfNull(lists);

        // A word on both kinds of list is refused.
        var spelled = new Dictionary<string, bool>(StringComparer.Ordinal);
        var asWritten = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var list in lists)
        {
            var refuse = list.Action == ChatFilterAction.Refuse;
            foreach (var entry in list.Words ?? [])
            {
                if (Normalize(entry) is { } word)
                {
                    var words = word.AsWritten ? asWritten : spelled;
                    words[word.Text] = refuse || (words.TryGetValue(word.Text, out var refused) && refused);
                }
            }
        }

        var byFirstLetter = spelled
            .GroupBy(pair => pair.Key[0])
            .ToFrozenDictionary(group => group.Key, group => group.Select(pair => new Entry(pair.Key, pair.Value)).ToArray());
        return new ChatFilter(byFirstLetter, asWritten);
    }

    /// <summary>
    /// The line as the room hears it: words from a mask list starred out. Refuses the line when it has a word from a
    /// refuse list.
    /// </summary>
    public string Apply(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (IsEmpty || text.Length == 0)
        {
            return text;
        }

        // The line is read once into word boundaries and letter positions, on the stack: nothing is allocated unless a
        // word is starred out.
        var small = text.Length <= StackLimit;
        Span<int> letters = small ? stackalloc int[text.Length] : new int[text.Length];
        Span<Word> words = small ? stackalloc Word[text.Length] : new Word[text.Length];
        Span<char> scratch = small ? stackalloc char[text.Length] : new char[text.Length];
        var wordCount = Split(text, letters, words);

        char[]? masked = null;
        for (var w = 0; w < wordCount; w++)
        {
            var word = words[w];
            var spelling = letters.Slice(word.FirstLetter, word.LetterCount);
            if (MatchWhole(text, spelling, scratch) is { } found)
            {
                Mask(text, spelling[found.From], spelling[found.From + found.Length - 1], ref masked);
            }

            // A run of single letters, spaced or dotted out ("b a d", "b.a.d"). Only single letters are joined, never
            // the end of one real word to the next. Their positions sit side by side in `letters`.
            if (word.LetterCount == 1 && (w == 0 || words[w - 1].LetterCount != 1))
            {
                var last = w;
                while (last + 1 < wordCount && words[last + 1].LetterCount == 1)
                {
                    last++;
                }

                if (last > w)
                {
                    MatchRun(text, letters.Slice(word.FirstLetter, last - w + 1), ref masked);
                }
            }
        }

        return masked is null ? text : new string(masked);
    }

    // Splits the line into words: runs of letters, digits and look-alikes. Apostrophes, combining accents and invisible
    // characters inside a word are part of it but no letter of it ("it's" is one word, "i t s"). Fills `letters` with
    // the position of every letter, word after word; returns the number of words.
    private static int Split(string text, Span<int> letters, Span<Word> words)
    {
        var letterCount = 0;
        var wordCount = 0;
        var i = 0;
        while (i < text.Length)
        {
            if (!IsLetterLike(text[i]))
            {
                i++;
                continue;
            }

            var first = letterCount;
            for (; i < text.Length && (IsLetterLike(text[i]) || IsIgnorable(text[i])); i++)
            {
                if (IsLetterLike(text[i]))
                {
                    letters[letterCount++] = i;
                }
            }

            words[wordCount++] = new Word(first, letterCount - first);
        }

        return wordCount;
    }

    // Whether a whole word is a listed one (refusing the line for a refused word). Symbols on the word's edges that
    // aren't letters may be punctuation ("darn!"), so the word is also tried without them. Returns which of its
    // letters to star out, or null.
    private (int From, int Length)? MatchWhole(string text, ReadOnlySpan<int> spelling, Span<char> scratch)
    {
        var lead = 0;
        while (lead < spelling.Length - 1 && !char.IsLetterOrDigit(text[spelling[lead]]))
        {
            lead++;
        }

        var trail = 0;
        while (trail < spelling.Length - 1 - lead && !char.IsLetterOrDigit(text[spelling[^(trail + 1)]]))
        {
            trail++;
        }

        (int From, int Length)? found = null;
        for (var from = 0; from <= lead; from += Math.Max(lead, 1))
        {
            for (var cut = 0; cut <= trail; cut += Math.Max(trail, 1))
            {
                var part = spelling[from..^cut];
                var listed = MatchesAsWritten(text, part, scratch);
                var spelled = MatchesSpelled(text, part, wholeOnly: true) > 0;
                if (listed || spelled)
                {
                    found ??= (from, part.Length);
                }
            }
        }

        return found;
    }

    // Spaced-out single letters: any stretch of two or more of them that spells a listed word.
    private void MatchRun(string text, ReadOnlySpan<int> run, ref char[]? masked)
    {
        var start = 0;
        while (start < run.Length - 1)
        {
            var length = MatchesSpelled(text, run[start..], wholeOnly: false);
            if (length >= 2)
            {
                Mask(text, run[start], run[start + length - 1], ref masked);
                start += length;
            }
            else
            {
                start++;
            }
        }
    }

    // The letters as typed, only folded (case, accents), against the words matched as written.
    private bool MatchesAsWritten(string text, ReadOnlySpan<int> spelling, Span<char> scratch)
    {
        if (_asWritten.Count == 0)
        {
            return false;
        }

        var folded = scratch[..spelling.Length];
        for (var i = 0; i < spelling.Length; i++)
        {
            folded[i] = Fold(text[spelling[i]]);
        }

        return _asWrittenLookup.TryGetValue(folded, out var refused) && Found(refused);
    }

    // How many letters from the start of `spelling` spell a listed word, the most if several do (all of them, when
    // `wholeOnly`); 0 when none does. Only words starting with a letter the first one can stand for are tried.
    private int MatchesSpelled(string text, ReadOnlySpan<int> spelling, bool wholeOnly)
    {
        var first = text[spelling[0]];
        var longest = Longest(text, spelling, Fold(first), wholeOnly);
        if (LookAlikes.TryGetValue(first, out var letters))
        {
            foreach (var letter in letters)
            {
                longest = Math.Max(longest, Longest(text, spelling, letter, wholeOnly));
            }
        }

        return longest;
    }

    private int Longest(string text, ReadOnlySpan<int> spelling, char firstLetter, bool wholeOnly)
    {
        var longest = 0;
        if (_byFirstLetter.TryGetValue(firstLetter, out var entries))
        {
            foreach (var entry in entries)
            {
                var length = Spells(text, spelling, entry.Word);
                if (length > 0 && (!wholeOnly || length == spelling.Length) && Found(entry.Refused))
                {
                    longest = Math.Max(longest, length);
                }
            }
        }

        return longest;
    }

    private static bool Found(bool refused) => refused ? throw new DomainException(RefusedMessage) : true;

    // The most letters from the start of `spelling` that spell `word` with each of its letters once or more ("baaad"),
    // or 0. reach[j]: the letters so far spell word[..j], the last of them word[j - 1].
    private static int Spells(string text, ReadOnlySpan<int> spelling, string word)
    {
        Span<bool> reach = word.Length < StackLimit ? stackalloc bool[word.Length + 1] : new bool[word.Length + 1];
        reach[0] = true;
        var longest = 0;
        for (var i = 0; i < spelling.Length; i++)
        {
            var c = text[spelling[i]];
            var any = false;
            for (var j = word.Length; j >= 1; j--)
            {
                reach[j] = (reach[j - 1] || reach[j]) && Stands(c, word[j - 1]);
                any |= reach[j];
            }

            reach[0] = false;
            if (reach[word.Length])
            {
                longest = i + 1;
            }

            if (!any)
            {
                break;
            }
        }

        return longest;
    }

    // Stars out everything from `first` to `last` but spaces.
    private static void Mask(string text, int first, int last, ref char[]? masked)
    {
        masked ??= text.ToCharArray();
        for (var i = first; i <= last; i++)
        {
            if (!char.IsWhiteSpace(masked[i]))
            {
                masked[i] = '*';
            }
        }
    }

    // A listed word, the way it is matched: spelled (letters folded, a look-alike read as its first letter), or as
    // written (only folded) when it has a character that is neither, has no letter, or comes to fewer than two letters.
    // Nothing is dropped, so a listed word never matches more than it says.
    private static (string Text, bool AsWritten)? Normalize(string? entry)
    {
        var trimmed = (entry ?? "").Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        var spelled = new StringBuilder();
        var written = new StringBuilder();
        var spellable = true;
        var hasLetter = false;
        foreach (var c in trimmed)
        {
            if (IsIgnorable(c))
            {
                continue;
            }

            written.Append(Fold(c));
            if (char.IsLetter(c))
            {
                spelled.Append(Fold(c));
                hasLetter = true;
            }
            else if (LookAlikes.TryGetValue(c, out var letters))
            {
                spelled.Append(letters[0]);
            }
            else
            {
                spellable = false;
            }
        }

        if (written.Length == 0)
        {
            return null;
        }

        return spellable && hasLetter && spelled.Length >= 2 ? (spelled.ToString(), false) : (written.ToString(), true);
    }

    private static bool IsLetterLike(char c) => char.IsLetterOrDigit(c) || LookAlikes.ContainsKey(c);

    // Inside a word but no letter of it: apostrophes, combining accents, zero-width characters.
    private static bool IsIgnorable(char c) =>
        c is '\'' or '’' or 'ʼ'
        || CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark
            or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.Format;

    // Whether the character in the text can stand for the word's letter.
    private static bool Stands(char c, char letter) =>
        Fold(c) == letter || (LookAlikes.TryGetValue(c, out var letters) && letters.Contains(letter, StringComparison.Ordinal));

    private static char Fold(char c)
    {
        var lower = char.ToLowerInvariant(c);
        return lower < 0x80 ? lower : Folded.GetValueOrDefault(lower, lower);
    }

    private static FrozenDictionary<char, char> BuildFolded()
    {
        var folded = new Dictionary<char, char>
        {
            // Cyrillic.
            ['а'] = 'a', // а
            ['в'] = 'b', // в
            ['е'] = 'e', // е
            ['ё'] = 'e', // ё
            ['к'] = 'k', // к
            ['м'] = 'm', // м
            ['н'] = 'h', // н
            ['о'] = 'o', // о
            ['р'] = 'p', // р
            ['с'] = 'c', // с
            ['т'] = 't', // т
            ['у'] = 'y', // у
            ['х'] = 'x', // х
            ['і'] = 'i', // і
            ['ї'] = 'i', // ї
            ['ј'] = 'j', // ј
            ['ѕ'] = 's', // ѕ
            ['ԁ'] = 'd', // ԁ
            ['ӏ'] = 'l', // ӏ
            ['ԛ'] = 'q', // ԛ
            ['ԝ'] = 'w', // ԝ
            // Greek.
            ['α'] = 'a', // α
            ['β'] = 'b', // β
            ['ε'] = 'e', // ε
            ['ι'] = 'i', // ι
            ['κ'] = 'k', // κ
            ['ν'] = 'v', // ν
            ['ο'] = 'o', // ο
            ['ρ'] = 'p', // ρ
            ['τ'] = 't', // τ
            ['υ'] = 'u', // υ
            ['χ'] = 'x', // χ
            ['ω'] = 'w', // ω
        };

        // Accented Latin letters: the letter they decompose to, when the rest is only accents.
        foreach (var (from, to) in new[] { ('À', 'ɏ'), ('Ḁ', 'ỿ') })
        {
            for (var c = from; c <= to; c++)
            {
                var lower = char.ToLowerInvariant(c);
                var parts = new string(lower, 1).Normalize(NormalizationForm.FormD);
                if (parts.Length > 1 && char.IsAsciiLetterLower(parts[0])
                    && parts.Skip(1).All(mark => CharUnicodeInfo.GetUnicodeCategory(mark) == UnicodeCategory.NonSpacingMark))
                {
                    folded.TryAdd(lower, parts[0]);
                }
            }
        }

        return folded.ToFrozenDictionary();
    }

    private sealed record Entry(string Word, bool Refused);

    // One word of the line: where its letters start among the letter positions, and how many it has.
    private readonly record struct Word(int FirstLetter, int LetterCount);
}

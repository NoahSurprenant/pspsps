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
/// hiding it: accents, full-width and other compatibility forms, and look-alike letters from other alphabets ("dárn");
/// digits and symbols for letters ("b4d"); a letter stretched three or more times ("baaad"); punctuation inside a word
/// ("b.ad", "b-a-d", "b*d"); and single letters spaced out ("b a d"). It isn't found inside a longer word ("class"
/// doesn't trip "ass"), across real words ("it's hit" doesn't spell "shit"), in a word that only doubles one of its
/// letters ("good" isn't "god"), or in a plain number ("455" isn't "ass").
/// </summary>
/// <remarks>
/// Each listed word is one word: letters, digits and look-alikes. One with a space or other punctuation is left out
/// and named in <see cref="Rejected"/>. A word with a digit that isn't a look-alike, with no letter, or of a single
/// letter ("69", "2g1c") is matched only as written, so it never widens to other words.
/// </remarks>
public sealed class ChatFilter
{
    public const string RefusedMessage = "Keep it friendly: that message has a word this game doesn't allow.";

    // Lines and words up to this long are checked on the stack; RoomState.MaxChatLength is well inside it.
    private const int StackLimit = 256;

    // Stands for any one letter, or none: "w*of".
    private const char Wildcard = '*';

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

    // Lower-case Cyrillic and Greek letters that pass for Latin ones. Accents and compatibility forms (full-width,
    // mathematical letters) are taken off by Unicode normalization first.
    private static readonly FrozenDictionary<char, char> Homoglyphs = new Dictionary<char, char>
    {
        // Cyrillic.
        ['а'] = 'a', // а
        ['в'] = 'b', // в
        ['е'] = 'e', // е
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
    }.ToFrozenDictionary();

    private readonly FrozenDictionary<char, Entry[]> _byFirstLetter;
    private readonly Entry[] _spelled;
    private readonly Dictionary<string, bool> _asWritten;
    private readonly Dictionary<string, bool>.AlternateLookup<ReadOnlySpan<char>> _asWrittenLookup;

    private ChatFilter(Entry[] spelled, Dictionary<string, bool> asWritten, IReadOnlyList<string> rejected)
    {
        _spelled = spelled;
        _byFirstLetter = spelled.GroupBy(entry => entry.Letters[0]).ToFrozenDictionary(group => group.Key, group => group.ToArray());
        _asWritten = asWritten;
        _asWrittenLookup = asWritten.GetAlternateLookup<ReadOnlySpan<char>>();
        Rejected = rejected;
    }

    /// <summary>A filter that lets everything through.</summary>
    public static ChatFilter None { get; } = Create([]);

    /// <summary>Whether no word is listed, so nothing is filtered.</summary>
    public bool IsEmpty => _spelled.Length == 0 && _asWritten.Count == 0;

    /// <summary>Listed words left out because they aren't one word (a space, or punctuation such as '-' or '*').</summary>
    public IReadOnlyList<string> Rejected { get; }

    public static ChatFilter Create(IEnumerable<ChatWordList> lists)
    {
        ArgumentNullException.ThrowIfNull(lists);

        // Word → refused. A word on both kinds of list is refused.
        var spelled = new Dictionary<string, bool>(StringComparer.Ordinal);
        var asWritten = new Dictionary<string, bool>(StringComparer.Ordinal);
        var rejected = new List<string>();
        foreach (var list in lists)
        {
            var refuse = list.Action == ChatFilterAction.Refuse;
            foreach (var entry in list.Words ?? [])
            {
                if (Normalize(entry) is not { } normalized)
                {
                    continue;
                }

                if (normalized.Kind == EntryKind.Rejected)
                {
                    rejected.Add(entry);
                    continue;
                }

                var words = normalized.Kind == EntryKind.AsWritten ? asWritten : spelled;
                words[normalized.Text] = refuse || (words.TryGetValue(normalized.Text, out var refused) && refused);
            }
        }

        return new ChatFilter([.. spelled.Select(pair => new Entry(pair.Key, pair.Value))], asWritten, rejected);
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

        // The line is read once into its letters and words, on the stack: nothing is allocated for a plain line.
        var capacity = text.Length * 2;
        var small = text.Length <= StackLimit;
        Span<Letter> letters = small ? stackalloc Letter[capacity] : new Letter[capacity];
        Span<Token> tokens = small ? stackalloc Token[text.Length] : new Token[text.Length];
        Span<char> scratch = small ? stackalloc char[capacity] : new char[capacity];
        var tokenCount = Read(text, letters, tokens);

        char[]? masked = null;
        for (var t = 0; t < tokenCount; t++)
        {
            var token = tokens[t];
            var spelling = letters.Slice(token.FirstLetter, token.LetterCount);
            if (MatchWhole(spelling, token.HasRealLetter, scratch) is { } found)
            {
                Mask(text, spelling[found.From].Start, spelling[found.From + found.Length - 1].End, ref masked);
            }

            // A run of single letters spaced out ("b a d"). Only single letters are joined across spaces, never one real
            // word to the next. Their letters sit side by side in `letters`, so the run is one slice of it.
            if (token.LetterCount == 1 && (t == 0 || tokens[t - 1].LetterCount != 1))
            {
                var last = t;
                while (last + 1 < tokenCount && tokens[last + 1].LetterCount == 1)
                {
                    last++;
                }

                if (last > t)
                {
                    MatchRun(text, letters.Slice(token.FirstLetter, last - t + 1), ref masked);
                }
            }
        }

        return masked is null ? text : new string(masked);
    }

    // Reads the line into words split at whitespace. A word's letters are its letters, digits, look-alikes and
    // wildcards, folded; other characters inside it (punctuation, accents, invisible characters) are skipped, so
    // "b.a-d" and "it's" are each one word. Returns the number of words.
    private static int Read(string text, Span<Letter> letters, Span<Token> tokens)
    {
        var letterCount = 0;
        var tokenCount = 0;
        var first = 0;
        var real = false;
        for (var i = 0; i < text.Length;)
        {
            _ = Rune.DecodeFromUtf16(text.AsSpan(i), out var rune, out var length);
            if (Rune.IsWhiteSpace(rune))
            {
                Close(tokens, ref tokenCount, ref first, letterCount, ref real);
            }
            else if (rune.Value < 0x80)
            {
                AddLetter((char)rune.Value, i, i + length, letters, ref letterCount, ref real);
            }
            else
            {
                // Compatibility forms (full-width, mathematical letters) to plain ones, accents split off and dropped.
                foreach (var c in rune.ToString().Normalize(NormalizationForm.FormKD))
                {
                    AddLetter(c, i, i + length, letters, ref letterCount, ref real);
                }
            }

            i += length;
        }

        Close(tokens, ref tokenCount, ref first, letterCount, ref real);
        return tokenCount;
    }

    private static void AddLetter(char c, int start, int end, Span<Letter> letters, ref int count, ref bool real)
    {
        if ((IsLetterLike(c) || c == Wildcard) && count < letters.Length)
        {
            letters[count++] = new Letter(start, end, Fold(c), c);
            real |= char.IsLetter(c);
        }
    }

    // Ends the word being read, if it has a letter.
    private static void Close(Span<Token> tokens, ref int tokenCount, ref int first, int letterCount, ref bool real)
    {
        if (letterCount > first)
        {
            tokens[tokenCount++] = new Token(first, letterCount - first, real);
        }

        first = letterCount;
        real = false;
    }

    // Whether a whole word is a listed one (refusing the line for a refused word). Symbols on the word's edges that
    // aren't letters may be punctuation ("darn!"), so the word is also tried without them. Returns which of its
    // letters to star out, or null.
    private (int From, int Length)? MatchWhole(ReadOnlySpan<Letter> spelling, bool hasRealLetter, Span<char> scratch)
    {
        var lead = 0;
        while (lead < spelling.Length - 1 && !char.IsLetterOrDigit(spelling[lead].Raw))
        {
            lead++;
        }

        var trail = 0;
        while (trail < spelling.Length - 1 - lead && !char.IsLetterOrDigit(spelling[^(trail + 1)].Raw))
        {
            trail++;
        }

        (int From, int Length)? found = null;
        for (var from = 0; from <= lead; from += Math.Max(lead, 1))
        {
            for (var cut = 0; cut <= trail; cut += Math.Max(trail, 1))
            {
                var part = spelling[from..^cut];
                var listed = MatchesAsWritten(part, scratch);

                // A plain number ("455") is never read through look-alikes.
                var spelled = hasRealLetter && MatchesSpelled(part, wholeOnly: true) > 0;
                if (listed || spelled)
                {
                    found ??= (from, part.Length);
                }
            }
        }

        return found;
    }

    // Spaced-out single letters: any stretch of two or more of them, with a real letter, that spells a listed word.
    private void MatchRun(string text, ReadOnlySpan<Letter> run, ref char[]? masked)
    {
        // Spaced-out digits ("4 5 5") are a number, not a word.
        var start = 0;
        while (start < run.Length - 1 && HasRealLetter(run[start..]))
        {
            var length = MatchesSpelled(run[start..], wholeOnly: false);
            if (length >= 2)
            {
                Mask(text, run[start].Start, run[start + length - 1].End, ref masked);
                start += length;
            }
            else
            {
                start++;
            }
        }
    }

    private static bool HasRealLetter(ReadOnlySpan<Letter> letters)
    {
        foreach (var letter in letters)
        {
            if (char.IsLetter(letter.Raw))
            {
                return true;
            }
        }

        return false;
    }

    // The letters as typed, only folded, against the words matched as written.
    private bool MatchesAsWritten(ReadOnlySpan<Letter> spelling, Span<char> scratch)
    {
        if (_asWritten.Count == 0)
        {
            return false;
        }

        var folded = scratch[..spelling.Length];
        for (var i = 0; i < spelling.Length; i++)
        {
            folded[i] = spelling[i].Folded;
        }

        return _asWrittenLookup.TryGetValue(folded, out var refused) && Found(refused);
    }

    // How many letters from the start of `spelling` spell a listed word, the most if several do (all of them, when
    // `wholeOnly`); 0 when none does. Only the words starting with a letter the first one can stand for are tried.
    private int MatchesSpelled(ReadOnlySpan<Letter> spelling, bool wholeOnly)
    {
        var first = spelling[0];
        if (first.Raw == Wildcard)
        {
            return Longest(spelling, _spelled, wholeOnly);
        }

        var longest = _byFirstLetter.TryGetValue(first.Folded, out var entries) ? Longest(spelling, entries, wholeOnly) : 0;
        if (LookAlikes.TryGetValue(first.Raw, out var letters))
        {
            foreach (var letter in letters)
            {
                if (letter != first.Folded && _byFirstLetter.TryGetValue(letter, out var more))
                {
                    longest = Math.Max(longest, Longest(spelling, more, wholeOnly));
                }
            }
        }

        return longest;
    }

    private static int Longest(ReadOnlySpan<Letter> spelling, Entry[] entries, bool wholeOnly)
    {
        var longest = 0;
        foreach (var entry in entries)
        {
            var length = Spells(spelling, entry);
            if (length > 0 && (!wholeOnly || length == spelling.Length) && Found(entry.Refused))
            {
                longest = Math.Max(longest, length);
            }
        }

        return longest;
    }

    private static bool Found(bool refused) => refused ? throw new DomainException(RefusedMessage) : true;

    // The most letters from the start of `spelling` that spell the entry, or 0. The entry is a series of runs of one
    // letter ("boob": b, oo, b); the text's run for each is exactly as long, or three or more and at least as long
    // ("baaad" for "bad", but "good" isn't "god"). A wildcard stands for any one letter or none.
    // State (r, m): the letters so far spell the entry up to run r, with m letters into run r (m capped at Caps[r]).
    private static int Spells(ReadOnlySpan<Letter> spelling, Entry entry)
    {
        var states = entry.StateCount;
        Span<bool> current = states <= StackLimit ? stackalloc bool[states] : new bool[states];
        Span<bool> next = states <= StackLimit ? stackalloc bool[states] : new bool[states];
        current.Clear();
        var runs = entry.RunLetters.Length;
        var canStart = true;
        var longest = 0;
        for (var i = 0; i < spelling.Length; i++)
        {
            var letter = spelling[i];
            var wildcard = letter.Raw == Wildcard;
            if (wildcard)
            {
                current.CopyTo(next);
            }
            else
            {
                next.Clear();
            }

            if (canStart && Stands(letter, entry.RunLetters[0]))
            {
                next[entry.Offsets[0]] = true;
            }

            for (var r = 0; r < runs; r++)
            {
                for (var m = 1; m <= entry.Caps[r]; m++)
                {
                    if (!current[entry.Offsets[r] + m - 1])
                    {
                        continue;
                    }

                    if (Stands(letter, entry.RunLetters[r]))
                    {
                        next[entry.Offsets[r] + Math.Min(m + 1, entry.Caps[r]) - 1] = true;
                    }

                    if (r + 1 < runs && entry.Complete(r, m) && Stands(letter, entry.RunLetters[r + 1]))
                    {
                        next[entry.Offsets[r + 1]] = true;
                    }
                }
            }

            canStart &= wildcard;
            next.CopyTo(current);

            var alive = false;
            for (var m = 1; m <= entry.Caps[runs - 1]; m++)
            {
                if (current[entry.Offsets[runs - 1] + m - 1] && entry.Complete(runs - 1, m))
                {
                    longest = i + 1;
                }
            }

            foreach (var state in current)
            {
                alive |= state;
            }

            if (!alive && !canStart)
            {
                break;
            }
        }

        return longest;
    }

    // Stars out everything from `start` to before `end` but spaces.
    private static void Mask(string text, int start, int end, ref char[]? masked)
    {
        masked ??= text.ToCharArray();
        for (var i = start; i < end; i++)
        {
            if (!char.IsWhiteSpace(masked[i]))
            {
                masked[i] = '*';
            }
        }
    }

    // A listed word, the way it is matched: spelled (letters folded, a look-alike read as its first letter), or as
    // written (only folded) when it has a digit that isn't a look-alike, has no letter, or comes to fewer than two
    // letters. Rejected when it has whitespace or punctuation; nothing is ever dropped from a word.
    private static (string Text, EntryKind Kind)? Normalize(string? entry)
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
        foreach (var rune in trimmed.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
            {
                return (trimmed, EntryKind.Rejected);
            }

            foreach (var c in rune.ToString().Normalize(NormalizationForm.FormKD))
            {
                if (IsLetterLike(c))
                {
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
                else if (!IsInsideAWord(c))
                {
                    return (trimmed, EntryKind.Rejected);
                }
            }
        }

        if (written.Length == 0)
        {
            return (trimmed, EntryKind.Rejected);
        }

        return spellable && hasLetter && spelled.Length >= 2
            ? (spelled.ToString(), EntryKind.Spelled)
            : (written.ToString(), EntryKind.AsWritten);
    }

    private static bool IsLetterLike(char c) => char.IsLetterOrDigit(c) || LookAlikes.ContainsKey(c);

    // Allowed in a listed word without being a letter of it: apostrophes, accents, invisible characters.
    private static bool IsInsideAWord(char c) =>
        c is '\'' or '’' or 'ʼ'
        || CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark
            or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.Format;

    // Whether a letter of the line can stand for a letter of a listed word.
    private static bool Stands(Letter letter, char wanted) =>
        letter.Raw == Wildcard
        || letter.Folded == wanted
        || (LookAlikes.TryGetValue(letter.Raw, out var letters) && letters.Contains(wanted, StringComparison.Ordinal));

    private static char Fold(char c)
    {
        var lower = char.ToLowerInvariant(c);
        return lower < 0x80 ? lower : Homoglyphs.GetValueOrDefault(lower, lower);
    }

    private enum EntryKind
    {
        Spelled,
        AsWritten,
        Rejected,
    }

    // A listed word, matched through look-alikes: its runs of one letter and the states to match them.
    private sealed class Entry
    {
        public Entry(string letters, bool refused)
        {
            Letters = letters;
            Refused = refused;
            var runLetters = new List<char>();
            var runCounts = new List<int>();
            foreach (var c in letters)
            {
                if (runLetters.Count > 0 && runLetters[^1] == c)
                {
                    runCounts[^1]++;
                }
                else
                {
                    runLetters.Add(c);
                    runCounts.Add(1);
                }
            }

            RunLetters = [.. runLetters];
            RunCounts = [.. runCounts];
            Caps = [.. runCounts.Select(count => Math.Max(count, 3))];
            Offsets = new int[Caps.Length];
            for (var r = 1; r < Caps.Length; r++)
            {
                Offsets[r] = Offsets[r - 1] + Caps[r - 1];
            }

            StateCount = Offsets[^1] + Caps[^1];
        }

        public string Letters { get; }
        public bool Refused { get; }
        public char[] RunLetters { get; }
        public int[] RunCounts { get; }
        public int[] Caps { get; }
        public int[] Offsets { get; }
        public int StateCount { get; }

        // A run is complete with exactly its letters, or stretched to three or more.
        public bool Complete(int run, int count) => count == RunCounts[run] || count >= Caps[run];
    }

    // One letter of the line: the characters it came from, and the character folded and as typed (after normalization).
    private readonly record struct Letter(int Start, int End, char Folded, char Raw);

    // One whitespace-delimited word: where its letters start, how many, and whether one is a real letter.
    private readonly record struct Token(int FirstLetter, int LetterCount, bool HasRealLetter);
}

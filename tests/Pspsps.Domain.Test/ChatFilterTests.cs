using Pspsps.Domain.Rooms;

namespace Pspsps.Domain.Test;

public sealed class ChatFilterTests
{
    private static readonly ChatFilter Filter = ChatFilter.Create(
    [
        new ChatWordList(ChatFilterAction.Mask, ["darn", "heck"]),
        new ChatWordList(ChatFilterAction.Refuse, ["woof"]),
    ]);

    [TestCase("darn", "****")]
    [TestCase("oh darn it", "oh **** it")]
    [TestCase("DARN!", "****!")]
    [TestCase("what the heck, darn", "what the ****, ****")]
    public void Apply_MasksListedWords(string text, string said)
    {
        Assert.That(Filter.Apply(text), Is.EqualTo(said));
    }

    [TestCase("oh d a r n", "oh * * * *")]
    [TestCase("d.a.r.n", "*******")]
    [TestCase("d-a_r*n it", "******* it")]
    [TestCase("h3ck", "****")]
    [TestCase("d@rn", "****")]
    [TestCase("daaaarrrn", "*********")]
    [TestCase("H 3 C K", "* * * *")]
    public void Apply_SeesThroughObfuscation(string text, string said)
    {
        Assert.That(Filter.Apply(text), Is.EqualTo(said));
    }

    [TestCase("woof")]
    [TestCase("a big WOOF from me")]
    [TestCase("w00f")]
    [TestCase("w o o f")]
    [TestCase("wooooof!")]
    public void Apply_RefusesLinesWithRefusedWords(string text)
    {
        var refused = Assert.Throws<DomainException>(() => Filter.Apply(text));

        Assert.That(refused!.Message, Is.EqualTo(ChatFilter.RefusedMessage));
    }

    [TestCase("pspsps")]
    [TestCase("darnation")]
    [TestCase("checkers")]
    [TestCase("a woofer")]
    [TestCase("hello, kitty")]
    [TestCase("")]
    public void Apply_LeavesOtherWordsAlone(string text)
    {
        Assert.That(Filter.Apply(text), Is.EqualTo(text));
    }

    [Test]
    public void Apply_InsideALongerWord_DoesNotMatch()
    {
        var filter = ChatFilter.Create([new ChatWordList(ChatFilterAction.Refuse, ["ass"])]);

        Assert.That(filter.Apply("first class grass"), Is.EqualTo("first class grass"));
        Assert.Throws<DomainException>(() => filter.Apply("you a$$"));
    }

    [Test]
    public void Create_NormalizesListedWords()
    {
        var filter = ChatFilter.Create([new ChatWordList(ChatFilterAction.Mask, ["  H3cK ", "", "--"])]);

        Assert.That(filter.Apply("heck"), Is.EqualTo("****"));
        Assert.That(filter.Apply("fine"), Is.EqualTo("fine"));
    }

    [Test]
    public void Create_AWordInBothLists_IsRefused()
    {
        var filter = ChatFilter.Create(
        [
            new ChatWordList(ChatFilterAction.Mask, ["heck"]),
            new ChatWordList(ChatFilterAction.Refuse, ["heck"]),
        ]);

        Assert.Throws<DomainException>(() => filter.Apply("heck"));
    }

    [Test]
    public void Apply_ALongLineOfRepeatsStaysQuick()
    {
        var filter = ChatFilter.Create([new ChatWordList(ChatFilterAction.Mask, ["aaaaab"])]);
        var line = new string('a', RoomState.MaxChatLength);

        Assert.That(filter.Apply(line), Is.EqualTo(line));
    }

    [TestCase("it's hit or miss")]
    [TestCase("this is it, hit it")]
    public void Apply_DoesNotSpellAWordAcrossRealWords(string text)
    {
        var filter = ChatFilter.Create([new ChatWordList(ChatFilterAction.Refuse, ["shit", "anal"])]);

        Assert.That(filter.Apply(text), Is.EqualTo(text));
        Assert.That(filter.Apply("an all-day event"), Is.EqualTo("an all-day event"));
    }

    [TestCase("s h i t")]
    [TestCase("s.h.i.t")]
    [TestCase("oh s-h-1-t")]
    public void Apply_StillSeesThroughSingleLettersSpacedOut(string text)
    {
        var filter = ChatFilter.Create([new ChatWordList(ChatFilterAction.Refuse, ["shit"])]);

        Assert.Throws<DomainException>(() => filter.Apply(text));
    }

    [Test]
    public void Create_AnEntryThatWouldShrink_IsMatchedAsWritten()
    {
        var filter = ChatFilter.Create([new ChatWordList(ChatFilterAction.Mask, ["69", "2g1c"])]);

        Assert.That(filter.Apply("gg"), Is.EqualTo("gg"));
        Assert.That(filter.Apply("g"), Is.EqualTo("g"));
        Assert.That(filter.Apply("gic 2glc zgic"), Is.EqualTo("gic 2glc zgic"));
        Assert.That(filter.Apply("69 and 2g1c"), Is.EqualTo("** and ****"));
    }

    [TestCase("d\u00e1rn", "****")]
    [TestCase("D\u00c0RN", "****")]
    [TestCase("da\u0301rn", "*****")]
    [TestCase("d\u0430rn", "****")]
    [TestCase("\u0434\u0430rn", "\u0434\u0430rn")]
    [TestCase("h\u0435\u0441k", "****")]
    public void Apply_SeesThroughAccentsAndLookAlikeLetters(string text, string said)
    {
        Assert.That(Filter.Apply(text), Is.EqualTo(said));
    }

    [Test]
    public void Apply_ALongListOnAChatLine_IsQuickAndAllocatesNothing()
    {
        // 2,000 made-up words, none of them in the line: the line is checked against every one of them.
        var words = Enumerable.Range(0, 2000).Select(MadeUpWord).ToList();
        var filter = ChatFilter.Create(
        [
            new ChatWordList(ChatFilterAction.Mask, words[..1000]),
            new ChatWordList(ChatFilterAction.Refuse, words[1000..]),
        ]);
        var line = string.Concat(Enumerable.Repeat("the quick brown cat naps all day in the sun ", 3))[..RoomState.MaxChatLength];
        for (var i = 0; i < 50; i++)
        {
            filter.Apply(line);
        }

        const int Runs = 500;
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < Runs; i++)
        {
            filter.Apply(line);
        }

        clock.Stop();
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;

        Assert.That(allocated, Is.LessThan(1024), "bytes allocated by all the runs together");
        Assert.That(clock.Elapsed.TotalMilliseconds / Runs, Is.LessThan(5), "milliseconds per line");
    }

    // A word of letters from a number, like a spreadsheet column: 0 is "baa", 1 "bab", ... (no word of the line).
    private static string MadeUpWord(int n)
    {
        var letters = new char[] { 'b', 'a', 'a', 'a' };
        for (var i = letters.Length - 1; i > 0 && n > 0; i--, n /= 26)
        {
            letters[i] = (char)('a' + (n % 26));
        }

        return new string(letters);
    }

    [Test]
    public void None_LetsEverythingThrough()
    {
        Assert.That(ChatFilter.None.Apply("darn woof"), Is.EqualTo("darn woof"));
    }
}

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
    public void Apply_ALongListOnAChatLine_AllocatesNothing()
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
        for (var i = 0; i < Runs; i++)
        {
            filter.Apply(line);
        }

        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;

        Assert.That(allocated, Is.LessThan(1024), "bytes allocated by all the runs together");
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

    [TestCase("wo-of")]
    [TestCase("wo.of")]
    [TestCase("wo_of")]
    [TestCase("w*of")]
    [TestCase("(w.o.o.f)")]
    public void Apply_SeesThroughPunctuationInsideAWord(string text)
    {
        Assert.Throws<DomainException>(() => Filter.Apply(text));
    }

    [TestCase("da~rn", "*****")]
    [TestCase("oh d-arn!", "oh *****!")]
    public void Apply_MasksThroughPunctuationInsideAWord(string text, string said)
    {
        Assert.That(Filter.Apply(text), Is.EqualTo(said));
    }

    [Test]
    public void Apply_SeesThroughPunctuationInsideAWord_ButNotAcrossSpaces()
    {
        var filter = ChatFilter.Create([new ChatWordList(ChatFilterAction.Refuse, ["shit", "anal"])]);

        Assert.Throws<DomainException>(() => filter.Apply("sh.it"));
        Assert.That(filter.Apply("it's hit or miss"), Is.EqualTo("it's hit or miss"));
        Assert.That(filter.Apply("an all-day event"), Is.EqualTo("an all-day event"));
    }

    [Test]
    public void Create_RefusesEntriesThatAreNotOneWord_AndSaysWhich()
    {
        var filter = ChatFilter.Create([new ChatWordList(ChatFilterAction.Mask, ["da-rn", "go away", "he*ck", "woof"])]);

        Assert.That(filter.Apply("darn heck woof"), Is.EqualTo("darn heck ****"));
    }

    [TestCase("I'm level 455")]
    [TestCase("717")]
    [TestCase("8008")]
    [TestCase("4 5 5")]
    public void Apply_PlainNumbersAreNotReadAsWords(string text)
    {
        var filter = ChatFilter.Create([new ChatWordList(ChatFilterAction.Refuse, ["ass", "tit", "boob"])]);

        Assert.That(filter.Apply(text), Is.EqualTo(text));
        Assert.Throws<DomainException>(() => filter.Apply("a55"));
    }

    [TestCase("\uFF57\uFF4F\uFF4F\uFF46")]
    [TestCase("\U0001D430\U0001D428\U0001D428\U0001D41F")]
    public void Apply_SeesThroughFullWidthAndMathLetters(string text)
    {
        Assert.Throws<DomainException>(() => Filter.Apply(text));
    }

    [Test]
    public void Apply_MasksAMathLetterWordWhole()
    {
        Assert.That(Filter.Apply("\U0001D41D\U0001D41A\U0001D42B\U0001D427!"), Is.EqualTo("********!"));
    }

    [TestCase("good", "good")]
    [TestCase("boob", "boob")]
    [TestCase("daarn", "daarn")]
    [TestCase("goood", "*****")]
    [TestCase("bbbob", "*****")]
    [TestCase("daaarn", "******")]
    public void Apply_OnlyThreeOrMoreOfALetterIsStretching(string text, string said)
    {
        var filter = ChatFilter.Create([new ChatWordList(ChatFilterAction.Mask, ["god", "bob", "darn"])]);

        Assert.That(filter.Apply(text), Is.EqualTo(said));
    }

    [Test]
    public void None_LetsEverythingThrough()
    {
        Assert.That(ChatFilter.None.Apply("darn woof"), Is.EqualTo("darn woof"));
    }
}

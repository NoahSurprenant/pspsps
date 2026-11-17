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
        var filter = ChatFilter.Create([new ChatWordList(ChatFilterAction.Mask, ["  He-Ck ", "", "--"])]);

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

    [Test]
    public void None_LetsEverythingThrough()
    {
        Assert.That(ChatFilter.None.Apply("darn woof"), Is.EqualTo("darn woof"));
    }
}

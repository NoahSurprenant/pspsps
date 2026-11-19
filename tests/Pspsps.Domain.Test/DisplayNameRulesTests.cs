using Pspsps.Domain.Players;
using Pspsps.Domain.Rooms;

namespace Pspsps.Domain.Test;

public sealed class DisplayNameRulesTests
{
    private static readonly ChatFilter Filter = ChatFilter.Create([
        new ChatWordList(ChatFilterAction.Mask, ["darn"]),
        new ChatWordList(ChatFilterAction.Refuse, ["heck"]),
    ]);

    [TestCase("Whiskers", "Whiskers")]
    [TestCase("  Sir   Fluff  ", "Sir Fluff")]
    [TestCase("cat_99-x", "cat_99-x")]
    public void Normalize_AcceptsAndTidies(string name, string expected) =>
        Assert.That(DisplayNameRules.Normalize(name), Is.EqualTo(expected));

    [TestCase(null)]
    [TestCase("ab")]
    [TestCase("abcdefghijklmnopqrstu")]
    [TestCase("bad!name")]
    [TestCase("émile")]
    public void Normalize_Refuses(string? name) =>
        Assert.Throws<DomainException>(() => DisplayNameRules.Normalize(name));

    [TestCase("Mr. Whiskers!!", "Mr Whiskers")]
    [TestCase("ねこ", "Kitty 1234")]
    [TestCase(null, "Kitty 1234")]
    [TestCase("a really really long discord name", "a really really long")]
    public void FromProfile_KeepsWhatItCanOrFallsBack(string? profileName, string expected) =>
        Assert.That(DisplayNameRules.FromProfile(profileName, "Kitty 1234"), Is.EqualTo(expected));

    [Test]
    public void EnsureAllowed_AcceptsAPlainName() =>
        Assert.DoesNotThrow(() => DisplayNameRules.EnsureAllowed("Whiskers", Filter));

    [Test]
    public void EnsureAllowed_RefusesANameTheFilterWouldMask()
    {
        var refused = Assert.Throws<DomainException>(() => DisplayNameRules.EnsureAllowed("Darn Cat", Filter));
        Assert.That(refused?.Message, Is.EqualTo(DisplayNameRules.FilteredMessage));
    }

    [Test]
    public void EnsureAllowed_RefusesANameTheFilterWouldRefuse()
    {
        var refused = Assert.Throws<DomainException>(() => DisplayNameRules.EnsureAllowed("Heck Cat", Filter));
        Assert.That(refused?.Message, Is.EqualTo(DisplayNameRules.FilteredMessage));
    }

    [TestCase("Admin Joe")]
    [TestCase("ADMIN")]
    [TestCase("Moderator Mike")]
    [TestCase("mod Squad")]
    public void EnsureAllowed_RefusesStaffImpersonation(string name)
    {
        var refused = Assert.Throws<DomainException>(() => DisplayNameRules.EnsureAllowed(name, Filter));
        Assert.That(refused?.Message, Is.EqualTo(DisplayNameRules.ImpersonatingMessage));
    }

    [Test]
    public void EnsureAllowed_AllowsAWordThatMerelyContainsModAsASubstringWithoutTheSpace() =>
        // "mod " (with the trailing space) doesn't catch "modern": only a whole word "mod" followed by another word.
        Assert.DoesNotThrow(() => DisplayNameRules.EnsureAllowed("Modern Cat", Filter));
}

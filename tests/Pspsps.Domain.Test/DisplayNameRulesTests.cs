using Pspsps.Domain.Players;

namespace Pspsps.Domain.Test;

public sealed class DisplayNameRulesTests
{
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
}

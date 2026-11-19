using Pspsps.Application.Moderation;
using Pspsps.Domain;

namespace Pspsps.Server.Test.Moderation;

public sealed class ModerationRequestTests
{
    [TestCase("mute", "1h", 1)]
    [TestCase("Mute", "24h", 24)]
    [TestCase("mute", "7d", 168)]
    [TestCase("ban", "24h", 24)]
    [TestCase("ban", "7d", 168)]
    public void Parse_TakesTheLengthsOnOffer(string action, string length, int hours) =>
        Assert.That(ModerationRequest.Parse(action, "Spam", length).Length, Is.EqualTo(TimeSpan.FromHours(hours)));

    [Test]
    public void Parse_ABanCanBeForGood() =>
        Assert.That(ModerationRequest.Parse("ban", "Spam", "permanent"),
            Is.EqualTo(new ModerationRequest(ModerationAction.Ban, "Spam", null)));

    [TestCase("warn", ModerationAction.Warn)]
    [TestCase("lift", ModerationAction.Lift)]
    [TestCase("rename", ModerationAction.Rename)]
    public void Parse_AWarningALiftOrARename_HasNoLength(string action, ModerationAction expected) =>
        Assert.That(ModerationRequest.Parse(action, "Be nice", "7d"), Is.EqualTo(new ModerationRequest(expected, "Be nice", null)));

    [TestCase("mute", "permanent")]
    [TestCase("mute", null)]
    [TestCase("ban", "1h")]
    [TestCase("ban", null)]
    [TestCase("banish", "7d")]
    [TestCase("1", "7d")]
    [TestCase("Mute, Ban", "7d")]
    [TestCase(null, null)]
    public void Parse_RefusesAnythingElse(string? action, string? length) =>
        Assert.Throws<DomainException>(() => ModerationRequest.Parse(action, "Spam", length));
}

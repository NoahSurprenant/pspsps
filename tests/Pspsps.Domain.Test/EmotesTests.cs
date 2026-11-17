using Pspsps.Domain.Rooms;

namespace Pspsps.Domain.Test;

public sealed class EmotesTests
{
    [Test]
    public void All_AreTheFourEmotes() =>
        Assert.That(Emotes.All, Is.EqualTo(new[] { "sit", "meow", "wave", "sleep" }));

    [TestCase("sit", CatPose.Sitting)]
    [TestCase("sleep", CatPose.Sleeping)]
    public void SitAndSleep_AreHeldPoses(string emote, CatPose pose)
    {
        Assert.That(Emotes.IsPose(emote), Is.True);
        Assert.That(Emotes.PoseOf(emote), Is.EqualTo(pose));
        Assert.That(Emotes.NameOf(pose), Is.EqualTo(emote));
    }

    [TestCase("meow")]
    [TestCase("wave")]
    [TestCase("dance")]
    public void OtherEmotes_AreNotPoses(string emote)
    {
        Assert.That(Emotes.IsPose(emote), Is.False);
        Assert.That(Emotes.PoseOf(emote), Is.Null);
    }

    [Test]
    public void Standing_HasNoName() => Assert.That(Emotes.NameOf(CatPose.Standing), Is.Null);

    [TestCase("sit", true)]
    [TestCase("wave", true)]
    [TestCase("Sit", false)]
    [TestCase("", false)]
    [TestCase(null, false)]
    public void IsKnown_OnlyTheFourByTheirExactNames(string? emote, bool known) =>
        Assert.That(Emotes.IsKnown(emote), Is.EqualTo(known));
}

using Pspsps.Domain.Safety;

namespace Pspsps.Domain.Test;

public sealed class MuteTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Tom = Guid.NewGuid();
    private static readonly Guid Felix = Guid.NewGuid();

    private static readonly MutesSoFar None = new(0, IncludesThem: false);

    [Test]
    public void Create_IsFromTheMuterOfTheMuted()
    {
        var mute = Mute.Create(Tom, Felix, None, Now);

        Assert.That((mute.MuterId, mute.MutedId, mute.MutedAt), Is.EqualTo((Tom, Felix, Now)));
    }

    [Test]
    public void Create_Yourself_IsRefused()
    {
        var refused = Assert.Throws<DomainException>(() => Mute.Create(Tom, Tom, None, Now));

        Assert.That(refused!.Message, Is.EqualTo("You can't mute yourself."));
    }

    [Test]
    public void Create_NoOne_IsRefused()
    {
        Assert.Throws<DomainException>(() => Mute.Create(Tom, Guid.Empty, None, Now));
        Assert.Throws<DomainException>(() => Mute.Create(Guid.Empty, Felix, None, Now));
    }

    [Test]
    public void Create_Twice_IsRefused()
    {
        var refused = Assert.Throws<DomainException>(() => Mute.Create(Tom, Felix, new MutesSoFar(1, IncludesThem: true), Now));

        Assert.That(refused!.Message, Is.EqualTo("You've muted that cat already."));
    }

    [Test]
    public void Create_UpToTheLimit_ThenRefused()
    {
        Assert.That(Mute.Create(Tom, Felix, new MutesSoFar(Mute.MaxMutes - 1, false), Now).MutedId, Is.EqualTo(Felix));

        var refused = Assert.Throws<DomainException>(() => Mute.Create(Tom, Felix, new MutesSoFar(Mute.MaxMutes, false), Now));
        Assert.That(refused!.Message, Does.Contain($"at most {Mute.MaxMutes}"));
    }

    [Test]
    public void Unmute_ByTheMuter_IsAllowed_ByAnyoneElse_Refused()
    {
        var mute = Mute.Create(Tom, Felix, None, Now);

        Assert.DoesNotThrow(() => mute.Unmute(Tom));
        var refused = Assert.Throws<DomainException>(() => mute.Unmute(Felix));
        Assert.That(refused!.Message, Is.EqualTo("That isn't your mute."));
    }
}

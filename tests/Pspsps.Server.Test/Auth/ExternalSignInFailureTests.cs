using Pspsps.Server.Auth;

namespace Pspsps.Server.Test.Auth;

public sealed class ExternalSignInFailureTests
{
    private static readonly Guid PlayerId = Guid.Parse("0193a5b2-7c1e-7d4f-9a3b-5e6f7a8b9c0d");

    [TestCase(false)]
    [TestCase(true)]
    public void ALinkThatFailed_SaysTheLinkFailed(bool signedIn) =>
        Assert.That(
            ExternalSignInFailure.RedirectFor(new ExternalSignInIntent("Google", PlayerId), signedIn),
            Is.EqualTo("/?link=failed"));

    [TestCase(false)]
    [TestCase(true)]
    public void ASignInThatFailed_SaysSignInFailed(bool signedIn) =>
        Assert.That(
            ExternalSignInFailure.RedirectFor(new ExternalSignInIntent("Discord"), signedIn),
            Is.EqualTo("/?signin=failed"));

    [Test]
    public void WithoutAnIntent_ASignedInPlayerWasLinking()
    {
        Assert.That(ExternalSignInFailure.RedirectFor(null, signedIn: true), Is.EqualTo("/?link=failed"));
        Assert.That(ExternalSignInFailure.RedirectFor(null, signedIn: false), Is.EqualTo("/?signin=failed"));
    }
}

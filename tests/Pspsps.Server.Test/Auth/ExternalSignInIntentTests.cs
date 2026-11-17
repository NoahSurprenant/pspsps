using Microsoft.AspNetCore.Authentication;
using Pspsps.Server.Auth;

namespace Pspsps.Server.Test.Auth;

public sealed class ExternalSignInIntentTests
{
    private static readonly Guid PlayerId = Guid.Parse("0193a5b2-7c1e-7d4f-9a3b-5e6f7a8b9c0d");

    [Test]
    public void ASignIn_RoundTripsWithoutAPlayer()
    {
        var properties = new ExternalSignInIntent("Discord").ToProperties();

        Assert.That(properties.RedirectUri, Is.EqualTo("/auth/callback"));
        Assert.That(ExternalSignInIntent.From(properties), Is.EqualTo(new ExternalSignInIntent("Discord")));
    }

    [Test]
    public void ALink_RoundTripsWithThePlayerWhoAsked()
    {
        var properties = new ExternalSignInIntent("Google", PlayerId).ToProperties();

        Assert.That(ExternalSignInIntent.From(properties), Is.EqualTo(new ExternalSignInIntent("Google", PlayerId)));
    }

    [Test]
    public void From_IsNullWithoutAProvider()
    {
        Assert.That(ExternalSignInIntent.From(null), Is.Null);
        Assert.That(ExternalSignInIntent.From(new AuthenticationProperties()), Is.Null);
    }

    [Test]
    public void From_RefusesALinkThatIsNotAPlayerId()
    {
        var properties = new ExternalSignInIntent("Google", PlayerId).ToProperties();
        properties.Items["link-player"] = "someone";

        Assert.That(ExternalSignInIntent.From(properties), Is.Null);
    }
}

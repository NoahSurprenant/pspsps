using Pspsps.Server.Auth;

namespace Pspsps.Server.Test.Auth;

public sealed class AuthRedirectsTests
{
    [Test]
    public void TheLinkResults_AreTheOnesTheClientKnows()
    {
        // client/src/link.ts LINK_RESULTS has the same four.
        string[] links = [AuthRedirects.Linked, AuthRedirects.Taken, AuthRedirects.Refused, AuthRedirects.LinkFailed];

        Assert.That(
            links.Select(l => l.Replace("/?link=", "", StringComparison.Ordinal)),
            Is.EquivalentTo(new[] { "linked", "taken", "refused", "failed" }));
        Assert.That(AuthRedirects.SignInFailed, Is.EqualTo("/?signin=failed"));
    }
}

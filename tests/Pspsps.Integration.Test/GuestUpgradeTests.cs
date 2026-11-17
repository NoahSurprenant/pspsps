using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Pspsps.Application.Players;
using Pspsps.Domain.Players;

namespace Pspsps.Integration.Test;

/// <summary>
/// "Save your cat" through the real endpoints: /auth/link challenges the provider, which <see cref="FakeExternalSignIn"/>
/// stands in for, and /auth/callback links the login to the signed-in guest.
/// </summary>
public sealed class GuestUpgradeTests
{
    private static GameFactory Game => PostgresFixture.Game;

    [Test]
    public async Task Link_KeepsTheGuestsCatAndCoinsAndTheLoginFindsItAfter()
    {
        using var browser = NewBrowser();
        var guest = await SignInGuestAsync(browser);
        await PlayerServiceTests.GiveCoinsAsync(guest.Id, 25);
        var key = PlayerServiceTests.NewKey();

        var landed = await FinishExternalSignInAsync(browser, FakeExternalSignIn.Url("Google", key, guest.Id));
        var me = await MeAsync(browser);

        // Later, in another browser: signing in with the login is the same cat.
        using var otherBrowser = NewBrowser();
        var otherLanded = await FinishExternalSignInAsync(otherBrowser, FakeExternalSignIn.Url("Google", key));
        var otherMe = await MeAsync(otherBrowser);

        Assert.Multiple(() =>
        {
            Assert.That(landed, Is.EqualTo("/?link=linked"));
            Assert.That(me, Is.EqualTo(guest with { Coins = 25, IsGuest = false }));
            Assert.That(otherLanded, Is.EqualTo("/"));
            Assert.That(otherMe, Is.EqualTo(me));
        });
    }

    [Test]
    public async Task Link_ToALoginThatHasACat_IsRefusedAndTheGuestStaysSignedIn()
    {
        var key = PlayerServiceTests.NewKey();
        var owner = await Game.Get<IPlayerService>()
            .SignInWithLoginAsync(LoginProvider.Discord, key, "Tom", CancellationToken.None);
        using var browser = NewBrowser();
        var guest = await SignInGuestAsync(browser);

        var landed = await FinishExternalSignInAsync(browser, FakeExternalSignIn.Url("Discord", key, guest.Id));
        var me = await MeAsync(browser);
        var ownerAfter = await Game.Get<IPlayerService>().FindAsync(owner.Id, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(landed, Is.EqualTo("/?link=taken"));
            Assert.That(me, Is.EqualTo(guest));
            Assert.That(ownerAfter, Is.EqualTo(owner));
        });
    }

    [Test]
    public async Task Link_FinishedAsAnotherPlayer_ChangesNothing()
    {
        using var browser = NewBrowser();
        var signedIn = await SignInGuestAsync(browser);
        var other = await Game.Get<IPlayerService>().CreateGuestAsync(CancellationToken.None);

        var landed = await FinishExternalSignInAsync(
            browser, FakeExternalSignIn.Url("Google", PlayerServiceTests.NewKey(), other.Id));
        var me = await MeAsync(browser);
        var otherAfter = await Game.Get<IPlayerService>().FindAsync(other.Id, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(landed, Is.EqualTo("/?link=failed"));
            Assert.That(me, Is.EqualTo(signedIn));
            Assert.That(otherAfter, Is.EqualTo(other));
        });
    }

    [Test]
    public async Task SignIn_WithoutLinking_StillMakesAnAccount()
    {
        using var browser = NewBrowser();

        var landed = await FinishExternalSignInAsync(
            browser, FakeExternalSignIn.Url("Discord", PlayerServiceTests.NewKey()));
        var me = await MeAsync(browser);

        Assert.That(landed, Is.EqualTo("/"));
        Assert.That(me, Has.Property(nameof(PlayerProfile.IsGuest)).False
            .And.Property(nameof(PlayerProfile.DisplayName)).EqualTo("Linked Cat"));
    }

    [Test]
    public async Task LinkEndpoint_SignedOut_SaysItFailed()
    {
        using var browser = NewBrowser();

        using var response = await PostLinkAsync(browser, "Discord", SameOrigin);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        Assert.That(response.Headers.Location?.OriginalString, Is.EqualTo("/?link=failed"));
    }

    [Test]
    public async Task LinkEndpoint_AProviderThatIsNotConfigured_IsNotFound()
    {
        using var browser = NewBrowser();
        await SignInGuestAsync(browser);

        using var response = await PostLinkAsync(browser, "Nowhere", SameOrigin);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [TestCase("Sec-Fetch-Site: same-origin")]
    [TestCase("Origin: http://localhost")]
    public async Task LinkEndpoint_FromThisSite_GoesToTheProvider(string header)
    {
        using var browser = NewBrowser();
        await SignInGuestAsync(browser);

        using var response = await PostLinkAsync(browser, "Discord", header);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        Assert.That(response.Headers.Location?.Host, Is.EqualTo("discord.com"));
    }

    [TestCase("Sec-Fetch-Site: cross-site")]
    [TestCase("Sec-Fetch-Site: same-site")]
    [TestCase("Sec-Fetch-Site: none")]
    [TestCase("Origin: https://elsewhere.example")]
    [TestCase(null)]
    public async Task LinkEndpoint_StartedByAnotherSite_IsRefused(string? header)
    {
        // A page elsewhere can send a signed-in guest here (the cookie is SameSite=Lax): only the game starts a link.
        using var browser = NewBrowser();
        await SignInGuestAsync(browser);

        using var response = await PostLinkAsync(browser, "Discord", header);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task LinkEndpoint_IsNotAGet()
    {
        using var browser = NewBrowser();
        await SignInGuestAsync(browser);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/auth/link/Discord", UriKind.Relative));
        request.Headers.Add("Sec-Fetch-Site", "same-origin");

        using var response = await browser.SendAsync(request);

        // The client app's page (the fallback), not a challenge: a link can't be started with a plain link.
        Assert.That(response.Headers.Location?.Host, Is.Not.EqualTo("discord.com"));
    }

    [Test]
    public async Task Link_WithAnEmptyProviderKey_Failed()
    {
        using var browser = NewBrowser();
        var guest = await SignInGuestAsync(browser);

        var landed = await FinishExternalSignInAsync(browser, FakeExternalSignIn.Url("Google", " ", guest.Id));

        // Not "refused": the cat has no Google login; the provider sent no user ID.
        Assert.That(landed, Is.EqualTo("/?link=failed"));
        Assert.That(await MeAsync(browser), Is.EqualTo(guest));
    }

    [Test]
    public async Task SignIn_WithAnEmptyProviderKey_Failed()
    {
        using var browser = NewBrowser();

        var landed = await FinishExternalSignInAsync(browser, FakeExternalSignIn.Url("Google", " "));

        Assert.That(landed, Is.EqualTo("/?signin=failed"));
        Assert.That(await MeAsync(browser), Is.Null);
    }

    [Test]
    public async Task Link_ASecondLoginFromTheSameProvider_IsRefused()
    {
        using var browser = NewBrowser();
        await FinishExternalSignInAsync(browser, FakeExternalSignIn.Url("Discord", PlayerServiceTests.NewKey()));
        var player = (await MeAsync(browser))!;

        var landed = await FinishExternalSignInAsync(
            browser, FakeExternalSignIn.Url("Discord", PlayerServiceTests.NewKey(), player.Id));
        var me = await MeAsync(browser);

        // Not "failed": trying again can't work, and the client says why.
        Assert.That(landed, Is.EqualTo("/?link=refused"));
        Assert.That(me, Is.EqualTo(player));
    }

    [Test]
    public async Task Link_ForAPlayerThatNoLongerExists_Failed()
    {
        using var browser = NewBrowser();
        var guest = await SignInGuestAsync(browser);
        await PlayerServiceTests.DeleteAsync(guest.Id);

        var landed = await FinishExternalSignInAsync(
            browser, FakeExternalSignIn.Url("Google", PlayerServiceTests.NewKey(), guest.Id));

        Assert.That(landed, Is.EqualTo("/?link=failed"));
    }

    [Test]
    public async Task Callback_WithoutTheExternalSignIn_WhileSignedIn_SaysTheLinkFailed()
    {
        // The external cookie expired (it lasts 10 minutes) or the browser dropped it: no intent to read, but a
        // signed-in player can only have been saving their cat.
        using var browser = NewBrowser();
        await SignInGuestAsync(browser);

        using var response = await browser.GetAsync(new Uri("/auth/callback", UriKind.Relative));

        Assert.That(response.Headers.Location?.OriginalString, Is.EqualTo("/?link=failed"));
    }

    [Test]
    public async Task Callback_WithoutTheExternalSignIn_SignedOut_SaysSignInFailed()
    {
        using var browser = NewBrowser();

        using var response = await browser.GetAsync(new Uri("/auth/callback", UriKind.Relative));

        Assert.That(response.Headers.Location?.OriginalString, Is.EqualTo("/?signin=failed"));
    }

    [Test]
    public async Task ProviderFailure_DuringALink_SaysTheLinkFailedAndClearsTheExternalCookie()
    {
        // The guest pressed Cancel on the provider's consent screen.
        using var browser = NewBrowser();
        var guest = await SignInGuestAsync(browser);

        using var response = await browser.GetAsync(FakeProviderHandler.Url("Google", guest.Id));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        Assert.That(response.Headers.Location?.OriginalString, Is.EqualTo("/?link=failed"));
        Assert.That(
            response.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : Enumerable.Empty<string>(),
            Has.Some.StartsWith("pspsps-external=;"));
        Assert.That(await MeAsync(browser), Is.EqualTo(guest));
    }

    [Test]
    public async Task ProviderFailure_DuringASignIn_SaysSignInFailed()
    {
        using var browser = NewBrowser();

        using var response = await browser.GetAsync(FakeProviderHandler.Url("Discord"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        Assert.That(response.Headers.Location?.OriginalString, Is.EqualTo("/?signin=failed"));
    }

    [Test]
    public async Task ProviderFailure_WithTheStateLost_WhileSignedIn_SaysTheLinkFailed()
    {
        using var browser = NewBrowser();
        await SignInGuestAsync(browser);

        using var response = await browser.GetAsync(FakeProviderHandler.Url());

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        Assert.That(response.Headers.Location?.OriginalString, Is.EqualTo("/?link=failed"));
    }

    private const string SameOrigin = "Sec-Fetch-Site: same-origin";

    /// <summary>The "Save your cat" button's form post, with one header the browser would add (or none).</summary>
    private static async Task<HttpResponseMessage> PostLinkAsync(HttpClient browser, string provider, string? header)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/auth/link/{provider}", UriKind.Relative));
        if (header is not null)
        {
            var colon = header.IndexOf(':', StringComparison.Ordinal);
            request.Headers.Add(header[..colon], header[(colon + 2)..]);
        }

        return await browser.SendAsync(request);
    }

    /// <summary>A browser: keeps its cookies, and lets the test see each redirect.</summary>
    private static HttpClient NewBrowser() =>
        Game.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task<PlayerProfile> SignInGuestAsync(HttpClient browser)
    {
        using var response = await browser.PostAsync(new Uri("/auth/guest", UriKind.Relative), null);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PlayerProfile>(JsonOptions.Web))!;
    }

    private static async Task<PlayerProfile?> MeAsync(HttpClient browser)
    {
        using var response = await browser.GetAsync(new Uri("/api/me", UriKind.Relative));
        return response.StatusCode == HttpStatusCode.OK
            ? await response.Content.ReadFromJsonAsync<PlayerProfile>(JsonOptions.Web)
            : null;
    }

    /// <summary>Comes back from the (fake) provider, follows it to /auth/callback, and returns where that sends us.</summary>
    private static async Task<string?> FinishExternalSignInAsync(HttpClient browser, Uri provider)
    {
        using var back = await browser.GetAsync(provider);
        Assert.That(back.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        using var callback = await browser.GetAsync(back.Headers.Location);
        Assert.That(callback.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        return callback.Headers.Location?.OriginalString;
    }
}

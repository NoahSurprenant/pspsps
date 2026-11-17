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

        using var response = await browser.GetAsync(new Uri("/auth/link/Google", UriKind.Relative));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        Assert.That(response.Headers.Location?.OriginalString, Is.EqualTo("/?link=failed"));
    }

    [Test]
    public async Task LinkEndpoint_AProviderThatIsNotConfigured_IsNotFound()
    {
        using var browser = NewBrowser();
        await SignInGuestAsync(browser);

        using var response = await browser.GetAsync(new Uri("/auth/link/Nowhere", UriKind.Relative));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
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

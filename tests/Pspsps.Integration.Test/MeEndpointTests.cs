using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Pspsps.Application.Players;

namespace Pspsps.Integration.Test;

/// <summary>/api/me: the client asks it on every page load, signed in or not.</summary>
public sealed class MeEndpointTests
{
    private static GameFactory Game => PostgresFixture.Game;

    [Test]
    public async Task SignedOut_IsNoContentNotAnError()
    {
        using var client = Game.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        using var response = await client.GetAsync(new Uri("/api/me", UriKind.Relative));

        // A 401 here shows as an error in every visitor's browser console before they've done anything.
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task SignedIn_ReturnsThePlayer()
    {
        using var client = Game.CreateClient();
        using var guest = await client.PostAsync(new Uri("/auth/guest", UriKind.Relative), null);
        var created = await guest.Content.ReadFromJsonAsync<PlayerProfile>(JsonOptions.Web);

        using var response = await client.GetAsync(new Uri("/api/me", UriKind.Relative));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await response.Content.ReadFromJsonAsync<PlayerProfile>(JsonOptions.Web), Is.EqualTo(created));
    }
}

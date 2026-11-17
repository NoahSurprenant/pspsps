using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Pspsps.Server.Auth;

namespace Pspsps.Integration.Test;

/// <summary>
/// Stands in for Discord and Google, which the tests don't have: <see cref="Path"/> does what a provider's handler does
/// when the player comes back, signing the external cookie in with the login and the intent the sign-in started with,
/// then sending the browser to the real /auth/callback.
/// </summary>
public sealed class FakeExternalSignIn : IStartupFilter
{
    public const string Path = "/test/external-sign-in";

    public static Uri Url(string provider, string key, Guid? linkToPlayer = null) => new(
        $"{Path}?provider={provider}&key={Uri.EscapeDataString(key)}"
        + (linkToPlayer is { } id ? $"&link={id}" : ""),
        UriKind.Relative);

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        // Not app.Map: that moves the path into PathBase, and the cookie would only be sent back under Path.
        app.Use(async (http, nextMiddleware) =>
        {
            if (http.Request.Path != Path)
            {
                await nextMiddleware(http);
                return;
            }

            var query = http.Request.Query;
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, query["key"].ToString()), new Claim(ClaimTypes.Name, "Linked Cat")],
                "fake-provider");
            var link = Guid.TryParse(query["link"].ToString(), out var playerId) ? playerId : (Guid?)null;
            var intent = new ExternalSignInIntent(query["provider"].ToString(), link);
            await http.SignInAsync(AuthSetup.ExternalScheme, new ClaimsPrincipal(identity), intent.ToProperties());
            http.Response.Redirect(ExternalSignInIntent.CallbackPath);
        });
        next(app);
    };
}

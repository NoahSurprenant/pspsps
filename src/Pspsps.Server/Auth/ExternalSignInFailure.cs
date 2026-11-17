using Microsoft.AspNetCore.Authentication;

namespace Pspsps.Server.Auth;

/// <summary>Where a Discord or Google sign-in that didn't work sends the player, so the client can say so.</summary>
public static class ExternalSignInFailure
{
    public const string LinkFailed = "/?link=failed";
    public const string SignInFailed = "/?signin=failed";

    /// <summary>
    /// A link that failed says so. Without an intent (the state was lost or the external cookie expired), a signed-in
    /// player can only have been saving their cat: the client shows a signed-in player the worlds, not the sign-in.
    /// </summary>
    public static string RedirectFor(ExternalSignInIntent? intent, bool signedIn) =>
        intent?.LinkToPlayer is not null || (intent is null && signedIn) ? LinkFailed : SignInFailed;

    /// <summary>
    /// The provider's callback failed (Cancel on the consent screen, an expired correlation cookie): back to the game
    /// with a message instead of an error page.
    /// </summary>
    public static async Task OnRemoteFailure(RemoteFailureContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var http = context.HttpContext;
        // The game cookie isn't read yet: a provider's callback is handled before the default authentication runs.
        var signedIn = (await http.AuthenticateAsync(AuthSetup.GameScheme)).Principal?.GetPlayerId() is not null;
        await http.SignOutAsync(AuthSetup.ExternalScheme);
        http.Response.Redirect(RedirectFor(ExternalSignInIntent.From(context.Properties), signedIn));
        context.HandleResponse();
    }
}

using Microsoft.AspNetCore.Authentication;

namespace Pspsps.Server.Auth;

/// <summary>Where a Discord or Google sign-in that didn't work sends the player, so the client can say so.</summary>
public static partial class ExternalSignInFailure
{
    /// <summary>
    /// A link that failed says so. Without an intent (the state was lost or the external cookie expired), a signed-in
    /// player can only have been saving their cat: the client shows a signed-in player the worlds, not the sign-in.
    /// </summary>
    public static string RedirectFor(ExternalSignInIntent? intent, bool signedIn) =>
        intent?.LinkToPlayer is not null || (intent is null && signedIn)
            ? AuthRedirects.LinkFailed
            : AuthRedirects.SignInFailed;

    /// <summary>
    /// The provider's callback failed (Cancel on the consent screen, an expired correlation cookie, a wrong client
    /// secret): back to the game with a message instead of an error page, and a warning in the logs so a broken
    /// provider setup still shows.
    /// </summary>
    public static async Task OnRemoteFailure(RemoteFailureContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var http = context.HttpContext;
        // The failure's message only: the handlers put no tokens or secrets in it.
        LogProviderFailed(
            http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ExternalSignInFailure)),
            context.Scheme.Name, context.Failure?.Message ?? "no reason given");
        // The game cookie isn't read yet: a provider's callback is handled before the default authentication runs.
        var signedIn = (await http.AuthenticateAsync(AuthSetup.GameScheme)).Principal?.GetPlayerId() is not null;
        await http.SignOutAsync(AuthSetup.ExternalScheme);
        http.Response.Redirect(RedirectFor(ExternalSignInIntent.From(context.Properties), signedIn));
        context.HandleResponse();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Signing in with {Provider} failed on the way back: {Reason}")]
    private static partial void LogProviderFailed(ILogger logger, string provider, string reason);
}

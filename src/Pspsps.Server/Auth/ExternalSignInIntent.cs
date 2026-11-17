using System.Globalization;
using Microsoft.AspNetCore.Authentication;

namespace Pspsps.Server.Auth;

/// <summary>
/// Why a Discord or Google sign-in started, carried through the provider's round trip in the protected
/// <see cref="AuthenticationProperties"/>: a plain sign-in, or "save my cat" (link the login to the player who asked).
/// </summary>
public sealed record ExternalSignInIntent(string Scheme, Guid? LinkToPlayer = null)
{
    public const string CallbackPath = "/auth/callback";

    private const string ProviderItem = "provider";
    private const string LinkItem = "link-player";

    public AuthenticationProperties ToProperties()
    {
        var properties = new AuthenticationProperties { RedirectUri = CallbackPath };
        properties.Items[ProviderItem] = Scheme;
        if (LinkToPlayer is { } playerId)
        {
            properties.Items[LinkItem] = playerId.ToString("D", CultureInfo.InvariantCulture);
        }

        return properties;
    }

    /// <summary>The intent a sign-in started with, or null when the properties don't say which provider it was.</summary>
    public static ExternalSignInIntent? From(AuthenticationProperties? properties)
    {
        if (properties is null
            || !properties.Items.TryGetValue(ProviderItem, out var scheme)
            || string.IsNullOrEmpty(scheme))
        {
            return null;
        }

        if (!properties.Items.TryGetValue(LinkItem, out var link) || link is null)
        {
            return new ExternalSignInIntent(scheme);
        }

        // A link item that isn't a player ID is not a plain sign-in either: refuse it rather than guess.
        return Guid.TryParse(link, CultureInfo.InvariantCulture, out var playerId)
            ? new ExternalSignInIntent(scheme, playerId)
            : null;
    }
}

namespace Pspsps.Server.Auth;

/// <summary>
/// Whether a request was sent by the game's own pages. Another site can send a signed-in player to a URL here (the
/// game cookie is SameSite=Lax), so a request that changes something on the player's behalf checks this first.
/// </summary>
public static class SameOriginRequest
{
    /// <summary>
    /// Sec-Fetch-Site says same-origin; a browser that doesn't send it sends Origin with a POST, which must be this
    /// host. Neither is not from here.
    /// </summary>
    public static bool IsFromThisSite(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var site = request.Headers["Sec-Fetch-Site"].ToString();
        if (site.Length > 0)
        {
            return site == "same-origin";
        }

        return Uri.TryCreate(request.Headers.Origin.ToString(), UriKind.Absolute, out var origin)
            && string.Equals(origin.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase);
    }
}

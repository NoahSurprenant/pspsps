namespace Pspsps.Server.Auth;

/// <summary>
/// Where sign-in and "Save your cat" send the player back to. The client reads the ?link= and ?signin= values
/// (client/src/link.ts, LINK_RESULTS): change them together.
/// </summary>
public static class AuthRedirects
{
    public const string Home = "/";
    public const string Linked = "/?link=linked";

    /// <summary>The login already has another cat.</summary>
    public const string Taken = "/?link=taken";

    /// <summary>The cat already has a login from that provider.</summary>
    public const string Refused = "/?link=refused";

    public const string LinkFailed = "/?link=failed";
    public const string SignInFailed = "/?signin=failed";

    /// <summary>The network the player signs in from is banned: the page asks why (/auth/ban) and shows it.</summary>
    public const string SignInBanned = "/?signin=banned";
}

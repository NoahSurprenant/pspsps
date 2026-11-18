using AspNet.Security.OAuth.Discord;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;

namespace Pspsps.Server.Auth;

/// <summary>
/// Two cookies: the game's own (who the player is, a year long) and a short one that holds a Discord or Google sign-in
/// only until /auth/callback turns it into a player. Discord and Google are on when their client ID is configured.
/// </summary>
public static class AuthSetup
{
    public const string GameScheme = "pspsps";
    public const string ExternalScheme = "pspsps-external";

    public static IReadOnlyList<string> AddGameAuthentication(this WebApplicationBuilder builder)
    {
        var auth = builder.Services.AddAuthentication(GameScheme)
            .AddCookie(GameScheme, options =>
            {
                options.Cookie.Name = "pspsps";
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.HttpOnly = true;
                options.ExpireTimeSpan = TimeSpan.FromDays(365);
                options.SlidingExpiration = true;
                // An API call or the hub without a cookie gets a 401, not a redirect to a login page.
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                // And a refusal (a banned player's hub connection) gets a 403, not a redirect to an access-denied page.
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            })
            .AddCookie(ExternalScheme, options =>
            {
                options.Cookie.Name = "pspsps-external";
                options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
            });

        var providers = new List<string>();
        var discord = builder.Configuration.GetSection("Authentication:Discord");
        if (!string.IsNullOrEmpty(discord["ClientId"]))
        {
            auth.AddDiscord(options =>
            {
                ConfigureExternal(options);
                options.ClientId = discord["ClientId"]!;
                options.ClientSecret = discord["ClientSecret"] ?? "";
            });
            providers.Add(DiscordAuthenticationDefaults.AuthenticationScheme);
        }

        var google = builder.Configuration.GetSection("Authentication:Google");
        if (!string.IsNullOrEmpty(google["ClientId"]))
        {
            auth.AddGoogle(options =>
            {
                ConfigureExternal(options);
                options.ClientId = google["ClientId"]!;
                options.ClientSecret = google["ClientSecret"] ?? "";
            });
            providers.Add(GoogleDefaults.AuthenticationScheme);
        }

        builder.Services.AddAuthorization();
        return providers;
    }

    public static AuthenticationProperties Persistent() => new() { IsPersistent = true };

    /// <summary>
    /// What every Discord and Google sign-in shares: it lands in the external cookie, and a failure on the way back
    /// returns to the game with a message.
    /// </summary>
    public static void ConfigureExternal(RemoteAuthenticationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.SignInScheme = ExternalScheme;
        options.Events.OnRemoteFailure = ExternalSignInFailure.OnRemoteFailure;
    }
}

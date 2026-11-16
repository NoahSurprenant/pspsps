using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Pspsps.Application.Players;
using Pspsps.Domain.Players;

namespace Pspsps.Server.Auth;

public static class AuthEndpoints
{
    private const string ProviderItem = "provider";

    public static void MapAuthEndpoints(this WebApplication app, IReadOnlyList<string> providers)
    {
        // Which sign-in buttons the client shows (the configured providers, by scheme name).
        app.MapGet("/auth/providers", () => providers);

        app.MapGet("/auth/login/{provider}", (string provider) =>
        {
            var scheme = providers.FirstOrDefault(p => string.Equals(p, provider, StringComparison.OrdinalIgnoreCase));
            if (scheme is null)
            {
                return Results.NotFound();
            }

            var properties = new AuthenticationProperties { RedirectUri = "/auth/callback" };
            properties.Items[ProviderItem] = scheme;
            return Results.Challenge(properties, [scheme]);
        });

        app.MapGet("/auth/callback", async (HttpContext http, IPlayerService players, CancellationToken ct) =>
        {
            var external = await http.AuthenticateAsync(AuthSetup.ExternalScheme);
            var key = external.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!external.Succeeded || key is null
                || external.Properties?.Items.TryGetValue(ProviderItem, out var scheme) != true
                || !Enum.TryParse<LoginProvider>(scheme, out var provider))
            {
                return Results.Redirect("/?signin=failed");
            }

            var name = external.Principal!.FindFirstValue(ClaimTypes.Name);
            var player = await players.SignInWithLoginAsync(provider, key, name, ct);
            await http.SignOutAsync(AuthSetup.ExternalScheme);
            await http.SignInAsync(AuthSetup.GameScheme, PlayerClaims.For(player), AuthSetup.Persistent());
            return Results.Redirect("/");
        });

        app.MapPost("/auth/guest", async (HttpContext http, IPlayerService players, CancellationToken ct) =>
        {
            var player = await players.CreateGuestAsync(ct);
            await http.SignInAsync(AuthSetup.GameScheme, PlayerClaims.For(player), AuthSetup.Persistent());
            return Results.Ok(player);
        });

        app.MapPost("/auth/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(AuthSetup.GameScheme);
            return Results.NoContent();
        });

        app.MapGet("/api/me", async (ClaimsPrincipal user, IPlayerService players, CancellationToken ct) =>
            user.GetPlayerId() is { } id && await players.FindAsync(id, ct) is { } player
                ? Results.Ok(player)
                : Results.Unauthorized());
    }
}

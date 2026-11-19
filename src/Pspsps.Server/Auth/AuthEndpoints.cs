using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Pspsps.Application.Moderation;
using Pspsps.Application.Players;
using Pspsps.Domain.Players;
using Pspsps.Server.Game;
using Pspsps.Server.Moderation;

namespace Pspsps.Server.Auth;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app, IReadOnlyList<string> providers)
    {
        // Which sign-in buttons the client shows (the configured providers, by scheme name).
        app.MapGet("/auth/providers", () => providers);

        app.MapGet("/auth/login/{provider}", (string provider) =>
            SchemeFor(providers, provider) is { } scheme
                ? Results.Challenge(new ExternalSignInIntent(scheme).ToProperties(), [scheme])
                : Results.NotFound());

        // "Save your cat": sign in with Discord or Google and add that login to the signed-in player, guest or not. A
        // POST from the game's own button: another site can't start a link for a signed-in player.
        app.MapPost("/auth/link/{provider}", (string provider, HttpRequest request, ClaimsPrincipal user) =>
        {
            if (!SameOriginRequest.IsFromThisSite(request))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            if (user.GetPlayerId() is not { } playerId)
            {
                return Results.Redirect(AuthRedirects.LinkFailed);
            }

            return SchemeFor(providers, provider) is { } scheme
                ? Results.Challenge(new ExternalSignInIntent(scheme, playerId).ToProperties(), [scheme])
                : Results.NotFound();
        });

        app.MapGet(ExternalSignInIntent.CallbackPath, async (
            HttpContext http, IPlayerService players, AddressGuard networks, CancellationToken ct) =>
        {
            var external = await http.AuthenticateAsync(AuthSetup.ExternalScheme);
            await http.SignOutAsync(AuthSetup.ExternalScheme);
            var key = external.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            var intent = ExternalSignInIntent.From(external.Properties);
            if (!external.Succeeded || string.IsNullOrWhiteSpace(key) || intent is null
                || !Enum.TryParse<LoginProvider>(intent.Scheme, out var provider))
            {
                return Results.Redirect(
                    ExternalSignInFailure.RedirectFor(intent, signedIn: http.User.GetPlayerId() is not null));
            }

            if (intent.LinkToPlayer is { } playerId)
            {
                return Results.Redirect(await LinkAsync(http.User, playerId, provider, key, players, ct));
            }

            // From a banned network, only an admin signs in (the owner can't lock themself out), and a new login gets no
            // cat: the page is sent to ask why, and shows it.
            if (networks.Enabled
                && await networks.BanOfAsync(http, await players.FindByLoginAsync(provider, key, ct), ct) is not null)
            {
                return Results.Redirect(AuthRedirects.SignInBanned);
            }

            var name = external.Principal!.FindFirstValue(ClaimTypes.Name);
            var player = await players.SignInWithLoginAsync(provider, key, name, ct);
            await networks.SeenAsync(http, player.Id);
            await http.SignInAsync(AuthSetup.GameScheme, PlayerClaims.For(player), AuthSetup.Persistent());
            return Results.Redirect(AuthRedirects.Home);
        });

        // A new guest, unless the network it comes from is banned (403, saying why and until when, as for a banned cat):
        // a banned guest who clears their cookies doesn't get a new cat from there.
        app.MapPost("/auth/guest", async (
            HttpContext http, IPlayerService players, AddressGuard networks, CancellationToken ct) =>
        {
            if (await networks.BanOfAsync(http, playerId: null, ct) is { } ban)
            {
                return Results.Json(BannedView.From(ban), statusCode: StatusCodes.Status403Forbidden);
            }

            var player = await players.CreateGuestAsync(ct);
            await networks.SeenAsync(http, player.Id);
            await http.SignInAsync(AuthSetup.GameScheme, PlayerClaims.For(player), AuthSetup.Persistent());
            return Results.Ok(player);
        });

        // Whether the network the page is on is banned, for the signed-in player (never an admin) or anyone: the ban (why,
        // until when), or 204. The page asks after a sign-in was refused for it (?signin=banned).
        app.MapGet("/auth/ban", async (HttpContext http, AddressGuard networks, CancellationToken ct) =>
            await networks.BanOfAsync(http, http.User.GetPlayerId(), ct) is { } ban
                ? Results.Ok(SanctionView.From(ban))
                : Results.NoContent());

        app.MapPost("/auth/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(AuthSetup.GameScheme);
            return Results.NoContent();
        });

        // Asked on every page load: not signed in is an answer (204), not an error the browser would log.
        // With whether they are an admin, and the ban and mute they are under: a banned player is shown why, not the game.
        // The ban is the cat's own, or else the network's it plays from (never an admin's).
        app.MapGet("/api/me", async (
            HttpContext http, IPlayerService players, IModerationService moderation, AddressGuard networks,
            CancellationToken ct) =>
        {
            if (http.User.GetPlayerId() is not { } id || await players.FindAsync(id, ct) is not { } player)
            {
                return Results.NoContent();
            }

            var standing = await moderation.StandingAsync(id, ct);
            if (standing.Ban is null && await networks.BanOfAsync(http, id, ct) is { } networkBan)
            {
                standing = standing with { Ban = networkBan };
            }

            return Results.Ok(MeView.From(player, moderation.IsAdmin(id), standing));
        });
    }

    private static string? SchemeFor(IReadOnlyList<string> providers, string provider) =>
        providers.FirstOrDefault(p => string.Equals(p, provider, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Links the login to the cat that asked, and says how it went (?link=linked, taken, refused or failed). The game
    /// cookie must still be that cat's: a link finished after signing out, or as someone else, changes nothing. The
    /// player stays signed in as themselves either way; a login that already has a cat is refused, never merged.
    /// </summary>
    private static async Task<string> LinkAsync(
        ClaimsPrincipal user, Guid playerId, LoginProvider provider, string key, IPlayerService players,
        CancellationToken ct)
    {
        if (user.GetPlayerId() != playerId)
        {
            return AuthRedirects.LinkFailed;
        }

        var outcome = await players.LinkLoginAsync(playerId, provider, key, ct);
        return outcome switch
        {
            LinkLoginOutcome.Linked => AuthRedirects.Linked,
            LinkLoginOutcome.TakenByAnotherPlayer => AuthRedirects.Taken,
            LinkLoginOutcome.Refused => AuthRedirects.Refused,
            _ => AuthRedirects.LinkFailed,
        };
    }
}

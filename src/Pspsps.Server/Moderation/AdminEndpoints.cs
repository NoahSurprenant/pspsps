using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Pspsps.Application.Moderation;
using Pspsps.Application.Rooms;
using Pspsps.Domain;
using Pspsps.Server.Auth;
using Pspsps.Server.Game;

namespace Pspsps.Server.Moderation;

/// <summary>
/// The admin page (<c>/admin</c>, the client's admin.html, for admins only) and what it reads and does
/// (<c>/api/admin/...</c>), and the rule that keeps a banned player off the game's hub.
/// </summary>
public static class AdminEndpoints
{
    /// <summary>The policy on the hub's endpoints: a banned player's connection is refused (403) before it opens.</summary>
    public const string NotBannedPolicy = "not-banned";

    /// <summary>The admin page as built (by the client) into wwwroot.</summary>
    public const string PageFile = "admin.html";

    public static IServiceCollection AddModeration(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ModerationOptions>(configuration.GetSection(ModerationOptions.SectionName));
        services.AddSingleton<IAdmins, ConfiguredAdmins>();
        services.AddSingleton<StandingCache>();
        // Network bans (Moderation:AddressKey, Moderation:AddressBans:Enabled): refuses to start when asked for with no key.
        services.AddSingleton(ClientAddresses.FromConfiguration(configuration));
        services.AddSingleton<AddressBanCache>();
        services.AddSingleton<AddressGuard>();
        services.AddHostedService<AddressPruning>();
        services.AddSingleton<PlayerConnections>();
        services.AddSingleton<ModerationOnThisPod>();
        // Several pods (Redis): a moderation reaches every pod's connections and standing reads through Redis pub/sub.
        if (configuration.GetConnectionString("Redis") is { Length: > 0 } redis)
        {
            services.AddSingleton(provider => new RedisModerationSignal(
                redis, provider.GetRequiredService<ModerationOnThisPod>(),
                provider.GetRequiredService<ILogger<RedisModerationSignal>>()));
            services.AddSingleton<IModerationSignal>(provider => provider.GetRequiredService<RedisModerationSignal>());
            services.AddHostedService(provider => provider.GetRequiredService<RedisModerationSignal>());
        }
        else
        {
            services.AddSingleton<IModerationSignal, LocalModerationSignal>();
        }

        services.AddSingleton<ModerationHubFilter>();
        services.AddSingleton<IModerationEffects, ModerationEffects>();
        services.AddSingleton<IAuthorizationHandler, NotBannedHandler>();
        services.AddAuthorizationBuilder().AddPolicy(NotBannedPolicy, policy => policy
            .RequireAuthenticatedUser()
            .AddRequirements(new NotBannedRequirement()));
        return services;
    }

    /// <summary>
    /// Sends a request for the built page's file name to <c>/admin</c>, which checks who is asking: the static files would
    /// serve it to anyone (it holds nothing but the page's script, which reads only what an admin may).
    /// </summary>
    public static IApplicationBuilder UseAdminPageGuard(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            if (context.Request.Path.Equals("/" + PageFile, StringComparison.OrdinalIgnoreCase))
            {
                context.Response.Redirect("/admin");
                return Task.CompletedTask;
            }

            return next(context);
        });

    public static void MapAdminEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Admins get the page; a player who isn't one is told so (403), with their own player ID, which is what goes in
        // the admin list; nobody signed in is sent to sign in first (401).
        app.MapGet("/admin", (ClaimsPrincipal user, IAdmins admins, IWebHostEnvironment environment) =>
        {
            if (user.GetPlayerId() is not { } playerId)
            {
                return Page(StatusCodes.Status401Unauthorized, "Sign in first",
                    "<p>Sign in to the game first (as a guest, too), then come back here.</p>");
            }

            if (!admins.Current.Contains(playerId))
            {
                return Page(StatusCodes.Status403Forbidden, "Not an admin",
                    "<p>This page is for the game's admins. Admins are listed by player ID in the server's settings "
                    + "(<code>Moderation:AdminPlayerIds</code>; the chart's <code>moderation.adminPlayerIds</code>).</p>"
                    + $"<p>Your player ID: <code id=\"player-id\">{playerId}</code></p>");
            }

            var file = environment.WebRootFileProvider.GetFileInfo(PageFile);
            return file.Exists
                ? Results.Stream(file.CreateReadStream(), "text/html; charset=utf-8")
                : Results.NotFound();
        });

        var admin = app.MapGroup("/api/admin");

        admin.MapGet("/overview", async (ClaimsPrincipal user, IModerationService moderation, IRoomLibrary rooms, CancellationToken ct) =>
            user.GetPlayerId() is { } playerId && moderation.IsAdmin(playerId)
                ? Results.Ok(AdminOverviewView.From(await moderation.OverviewAsync(playerId, ct), rooms))
                : Results.StatusCode(StatusCodes.Status403Forbidden));

        // From the admin page only (Sec-Fetch-Site or Origin): another site can't act through an admin's cookie.
        admin.MapPost("/players/{playerId:guid}/moderate", async (
            Guid playerId, ModerateRequestView body, HttpRequest request, ClaimsPrincipal user, IModerationService moderation,
            IAddressBanService networks, IModerationEffects effects, CancellationToken ct) =>
        {
            if (!SameOriginRequest.IsFromThisSite(request) || user.GetPlayerId() is not { } adminId
                || !moderation.IsAdmin(adminId))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            try
            {
                var asked = ModerationRequest.Parse(body.Action, body.Reason, body.Length);
                // Not the request's token: a change saved must be told, even if the admin's page goes meanwhile.
                var standing = await moderation.ModerateAsync(adminId, playerId, asked, CancellationToken.None);
                await effects.AppliedAsync(playerId, asked.Action, standing);
                if (asked.Action == ModerationAction.Lift && body.LiftNetworks)
                {
                    effects.NetworksLifted(await networks.LiftOfPlayerAsync(adminId, playerId, CancellationToken.None));
                }

                return Results.Ok(new CardModerationView(
                    moderation.IsAdmin(playerId), SanctionView.From(standing.Ban), SanctionView.From(standing.Mute)));
            }
            catch (DomainException e)
            {
                return Results.BadRequest(new { message = e.Message });
            }
        });

        admin.MapRoomEditor();
        admin.MapRoomPreview();
        MapReportDecisions(admin);
        MapAddressLifts(admin);
    }

    // Lifting a network's ban, from the admin page only (as above).
    private static void MapAddressLifts(RouteGroupBuilder admin) =>
        admin.MapPost("/networks/{addressHash}/lift", async (
            string addressHash, HttpRequest request, ClaimsPrincipal user, IModerationService moderation,
            IAddressBanService networks, IModerationEffects effects) =>
        {
            if (!SameOriginRequest.IsFromThisSite(request) || user.GetPlayerId() is not { } adminId
                || !moderation.IsAdmin(adminId))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            try
            {
                await networks.LiftAsync(adminId, addressHash, CancellationToken.None);
                effects.NetworksLifted([addressHash]);
                return Results.NoContent();
            }
            catch (DomainException e)
            {
                return Results.BadRequest(new { message = e.Message });
            }
        });

    // The decisions on reports, from the admin page only (as above).
    private static void MapReportDecisions(RouteGroupBuilder admin) =>
        admin.MapPost("/reports/{reportId:guid}/decide", async (
            Guid reportId, ModerateRequestView body, HttpRequest request, ClaimsPrincipal user, IModerationService moderation,
            IModerationEffects effects) =>
        {
            if (!SameOriginRequest.IsFromThisSite(request) || user.GetPlayerId() is not { } adminId
                || !moderation.IsAdmin(adminId))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            try
            {
                // "dismiss", or "warn", "mute" or "ban" the reported player (which closes the report with it).
                var asked = ModerationRequest.ForReport(body.Action, body.Reason, body.Length);
                var decided = await moderation.DecideReportAsync(adminId, reportId, asked, CancellationToken.None);
                if (decided.Action is { } action)
                {
                    await effects.AppliedAsync(decided.ReportedId, action, decided.Standing);
                }

                return Results.NoContent();
            }
            catch (DomainException e)
            {
                return Results.BadRequest(new { message = e.Message });
            }
        });

    // A small page of the server's own: the game's colours, readable on a phone.
    private static IResult Page(int status, string title, string body) => Results.Content(
        $$"""
        <!doctype html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>{{WebUtility.HtmlEncode(title)}} · pspsps</title>
        <style>
        body { margin: 0; padding: 24px 16px; font: 16px/1.5 system-ui, sans-serif; background: #bfe3f2; color: #2a2230; }
        main { max-width: 36rem; margin: 0 auto; background: #fffaf0; border: 2px solid #2a2230; border-radius: 12px; padding: 16px 20px; }
        code { overflow-wrap: anywhere; font-size: 15px; }
        a { color: #1d4e89; }
        </style>
        </head>
        <body><main><h1>{{WebUtility.HtmlEncode(title)}}</h1>{{body}}<p><a href="/">Back to the game</a></p></main></body>
        </html>
        """, "text/html; charset=utf-8", statusCode: status);
}

/// <summary>The hub's rule that a banned player can't connect.</summary>
public sealed class NotBannedRequirement : IAuthorizationRequirement;

/// <summary>
/// Allows a hub connection unless its player is banned now (<see cref="StandingCache"/>), or the network it comes from is
/// (<see cref="AddressGuard"/>; never for an admin).
/// </summary>
internal sealed class NotBannedHandler(StandingCache standings, AddressGuard networks)
    : AuthorizationHandler<NotBannedRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, NotBannedRequirement requirement)
    {
        if (context.User.GetPlayerId() is not { } playerId
            || await standings.BanOfAsync(playerId, CancellationToken.None) is not null)
        {
            return;
        }

        // The hub's endpoints are authorized with the request as the resource.
        if (context.Resource is HttpContext http
            && await networks.BanOfAsync(http, playerId, CancellationToken.None) is not null)
        {
            return;
        }

        context.Succeed(requirement);
    }
}

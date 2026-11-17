using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pspsps.Server.Auth;

namespace Pspsps.Integration.Test;

/// <summary>
/// A provider whose return always fails, as Discord or Google's does when the player presses Cancel on the consent
/// screen or the sign-in's state is lost: its callback (<see cref="CallbackPath"/>) goes through the same remote
/// failure handling as theirs. The query says what intent the sign-in started with; without "provider", the state was
/// lost.
/// </summary>
public sealed class FakeProviderHandler(
    IOptionsMonitor<RemoteAuthenticationOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : RemoteAuthenticationHandler<RemoteAuthenticationOptions>(options, logger, encoder)
{
    public const string SchemeName = "FakeProvider";
    public const string CallbackPath = "/signin-fake";

    public static Uri Url(string? provider = null, Guid? linkToPlayer = null) => new(
        CallbackPath + "?error=access_denied"
        + (provider is null ? "" : $"&provider={provider}")
        + (linkToPlayer is { } id ? $"&link={id}" : ""),
        UriKind.Relative);

    protected override Task<HandleRequestResult> HandleRemoteAuthenticateAsync()
    {
        var query = Request.Query;
        var provider = query["provider"].ToString();
        var link = Guid.TryParse(query["link"].ToString(), out var playerId) ? playerId : (Guid?)null;
        var properties = provider.Length == 0 ? null : new ExternalSignInIntent(provider, link).ToProperties();
        return Task.FromResult(HandleRequestResult.Fail("Access was denied by the resource owner.", properties));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
        throw new NotSupportedException("The tests come back from the fake provider; they never go to it.");
}

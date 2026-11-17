using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Pspsps.Server.Auth;

namespace Pspsps.Server.Test.Auth;

public sealed class ExternalSignInFailureTests
{
    private static readonly Guid PlayerId = Guid.Parse("0193a5b2-7c1e-7d4f-9a3b-5e6f7a8b9c0d");

    [TestCase(false)]
    [TestCase(true)]
    public void ALinkThatFailed_SaysTheLinkFailed(bool signedIn) =>
        Assert.That(
            ExternalSignInFailure.RedirectFor(new ExternalSignInIntent("Google", PlayerId), signedIn),
            Is.EqualTo("/?link=failed"));

    [TestCase(false)]
    [TestCase(true)]
    public void ASignInThatFailed_SaysSignInFailed(bool signedIn) =>
        Assert.That(
            ExternalSignInFailure.RedirectFor(new ExternalSignInIntent("Discord"), signedIn),
            Is.EqualTo("/?signin=failed"));

    [Test]
    public void WithoutAnIntent_ASignedInPlayerWasLinking()
    {
        Assert.That(ExternalSignInFailure.RedirectFor(null, signedIn: true), Is.EqualTo("/?link=failed"));
        Assert.That(ExternalSignInFailure.RedirectFor(null, signedIn: false), Is.EqualTo("/?signin=failed"));
    }

    [Test]
    public async Task OnRemoteFailure_LogsAWarningWithTheProviderAndWhy()
    {
        // A wrong client secret or a proxy that loses the correlation cookie: the player gets a message, and the
        // logs still say what broke.
        var logs = new ListLoggerProvider();
        var authentication = Substitute.For<IAuthenticationService>();
        authentication.AuthenticateAsync(Arg.Any<HttpContext>(), Arg.Any<string?>())
            .Returns(AuthenticateResult.NoResult());
        using var services = new ServiceCollection()
            .AddLogging(logging => logging.AddProvider(logs))
            .AddSingleton(authentication)
            .BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        var scheme = new AuthenticationScheme("Google", null, typeof(IAuthenticationHandler));
        var context = new RemoteFailureContext(
            http, scheme, new RemoteAuthenticationOptions(),
            new AuthenticationFailureException("OAuth token endpoint failure: Status: BadRequest"))
        {
            Properties = new ExternalSignInIntent("Google").ToProperties(),
        };

        await ExternalSignInFailure.OnRemoteFailure(context);

        Assert.That(http.Response.Headers.Location.ToString(), Is.EqualTo("/?signin=failed"));
        Assert.That(
            logs.Entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.Message),
            Has.One.Contains("Google").And.Contains("OAuth token endpoint failure"));
    }

    private sealed class ListLoggerProvider : ILoggerProvider
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => new ListLogger(Entries);

        public void Dispose()
        {
        }

        private sealed class ListLogger(List<(LogLevel Level, string Message)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                entries.Add((logLevel, formatter(state, exception)));
        }
    }
}

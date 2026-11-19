using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Pspsps.Integration.Test;

/// <summary>
/// The address a test's request comes from, as a TCP peer's would: the test server has none, so a request with
/// <see cref="Header"/> gets that address as its connection's remote address, before anything of the server's runs
/// (UseForwardedHeaders included, which then treats it as it would a real peer). Requests without it have no address.
/// The CI server of the end-to-end tests sees one address for every browser, so network bans are tested here.
/// </summary>
public sealed class TestPeer : IStartupFilter
{
    public const string Header = "X-Test-Peer";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((http, nextMiddleware) =>
        {
            if (IPAddress.TryParse(http.Request.Headers[Header].ToString(), out var peer))
            {
                http.Connection.RemoteIpAddress = peer;
            }

            return nextMiddleware(http);
        });
        next(app);
    };
}

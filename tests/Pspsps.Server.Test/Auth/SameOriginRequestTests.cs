using Microsoft.AspNetCore.Http;
using Pspsps.Server.Auth;

namespace Pspsps.Server.Test.Auth;

public sealed class SameOriginRequestTests
{
    [TestCase("same-origin", null, ExpectedResult = true)]
    [TestCase("same-site", null, ExpectedResult = false)]
    [TestCase("cross-site", null, ExpectedResult = false)]
    [TestCase("none", null, ExpectedResult = false)]
    [TestCase("cross-site", "https://pspsps.example", ExpectedResult = false)]
    [TestCase(null, "https://pspsps.example", ExpectedResult = true)]
    [TestCase(null, "https://PSPSPS.example:443", ExpectedResult = true)]
    [TestCase(null, "https://elsewhere.example", ExpectedResult = false)]
    [TestCase(null, "https://pspsps.example.elsewhere.example", ExpectedResult = false)]
    [TestCase(null, "null", ExpectedResult = false)]
    [TestCase(null, null, ExpectedResult = false)]
    public bool IsFromThisSite(string? secFetchSite, string? origin)
    {
        var request = new DefaultHttpContext().Request;
        request.Host = new HostString("pspsps.example");
        if (secFetchSite is not null)
        {
            request.Headers["Sec-Fetch-Site"] = secFetchSite;
        }

        if (origin is not null)
        {
            request.Headers.Origin = origin;
        }

        return SameOriginRequest.IsFromThisSite(request);
    }
}

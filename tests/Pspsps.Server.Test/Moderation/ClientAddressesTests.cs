using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Pspsps.Server.Moderation;
using Pspsps.Server.Telemetry;

namespace Pspsps.Server.Test.Moderation;

public sealed class ClientAddressesTests
{
    private const string Key = "a-test-key-of-at-least-32-characters";
    private static readonly ClientAddresses Addresses = ClientAddresses.WithKey(Key);

    private static string Hash(string address) => Addresses.HashOf(IPAddress.Parse(address));

    [Test]
    public void AnIPv4Address_IsHashedWhole_WithTheKey_TheSameEveryTime()
    {
        var hash = Hash("192.0.2.1");

        Assert.Multiple(() =>
        {
            Assert.That(hash, Does.Match("^[0-9a-f]{64}$"));
            Assert.That(Hash("192.0.2.1"), Is.EqualTo(hash), "stable");
            Assert.That(Hash("192.0.2.2"), Is.Not.EqualTo(hash), "the whole address: a neighbour is another network");
            Assert.That(hash, Is.EqualTo(Convert.ToHexStringLower(
                HMACSHA256.HashData(Encoding.UTF8.GetBytes(Key), new byte[] { 192, 0, 2, 1 }))), "HMAC-SHA256 of the address's bytes");
            Assert.That(Hash("::ffff:192.0.2.1"), Is.EqualTo(hash), "written as IPv6, the same address");
        });
    }

    [Test]
    public void AnIPv6Address_IsHashedByIts64()
    {
        var hash = Hash("2001:db8:1:2::1");

        Assert.Multiple(() =>
        {
            Assert.That(Hash("2001:db8:1:2:aaaa:bbbb:cccc:dddd"), Is.EqualTo(hash), "anywhere in the /64");
            Assert.That(Hash("2001:db8:1:3::1"), Is.Not.EqualTo(hash), "another /64");
            Assert.That(ClientAddresses.NetworkOf(IPAddress.Parse("2001:db8:1:2:aaaa:bbbb:cccc:dddd")),
                Is.EqualTo(IPAddress.Parse("2001:db8:1:2::")));
        });
    }

    [Test]
    public void AnotherKey_MakesAnotherHash()
    {
        var other = ClientAddresses.WithKey("another-test-key-of-32-characters!!");

        Assert.That(other.HashOf(IPAddress.Parse("192.0.2.1")), Is.Not.EqualTo(Hash("192.0.2.1")));
    }

    [Test]
    public void TheConfiguration_TurnsNetworkBansOn_OnlyWithAKey_AndRefusesToStartWithoutOneWhenAskedFor()
    {
        static ClientAddresses From(string? key, string? enabled) => ClientAddresses.FromConfiguration(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ClientAddresses.KeyName] = key,
                [ClientAddresses.EnabledName] = enabled,
            }).Build());

        Assert.Multiple(() =>
        {
            Assert.That(From(Key, null).Enabled, Is.True, "on when the key is set");
            Assert.That(From(null, null).Enabled, Is.False, "off without it");
            Assert.That(From(Key, "false").Enabled, Is.False, "off when turned off");
            Assert.That(From(Key, "true").Enabled, Is.True);
        });
        var noKey = Assert.Throws<InvalidOperationException>(() => From(null, "true"));
        Assert.That(noKey!.Message, Does.Contain(ClientAddresses.KeyName));
        Assert.Throws<InvalidOperationException>(() => From("too-short", null));
        Assert.Throws<InvalidOperationException>(() => From(Key, "yes"));
    }

    // Loopback, private, link-local, unique-local and shared (carrier) addresses: the server's own side of a proxy or a
    // local network, never a player's network on the internet. Behind a proxy whose forwarded header isn't trusted,
    // every player would look like one of these: banning it would lock everyone out.
    [TestCase("127.0.0.1")]
    [TestCase("::1")]
    [TestCase("10.0.0.1")]
    [TestCase("172.16.0.1")]
    [TestCase("172.31.255.254")]
    [TestCase("192.168.0.1")]
    [TestCase("169.254.0.1")]
    [TestCase("100.64.0.1")]
    [TestCase("fe80::1")]
    [TestCase("fd00::1")]
    [TestCase("::ffff:192.168.0.1")]
    public void AnAddressThatIsntOnTheInternet_IsNeverAPlayersNetwork(string address)
    {
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse(address);

        Assert.That(Addresses.HashOf(http), Is.Null);
    }

    [TestCase("192.0.2.1")]
    [TestCase("172.32.0.1")]
    [TestCase("2001:db8::1")]
    public void AnAddressOnTheInternet_IsAPlayersNetwork(string address)
    {
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse(address);

        Assert.That(Addresses.HashOf(http), Is.EqualTo(Hash(address)));
    }

    [Test]
    public void NetworkBansOn_WithNoTrustedProxy_AreWarnedOfAtStartup()
    {
        static string? Warning(Dictionary<string, string?> settings) =>
            ClientAddresses.StartupWarning(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());

        var on = new Dictionary<string, string?> { [ClientAddresses.KeyName] = Key };
        Assert.That(Warning(on), Does.Contain("ForwardedHeaders"));
        Assert.That(Warning(new(on) { ["ForwardedHeaders:KnownIPNetworks:0"] = "192.0.2.0/24" }), Is.Null);
        Assert.That(Warning(new(on) { ["ForwardedHeaders:KnownProxies:0"] = "192.0.2.10" }), Is.Null);
        Assert.That(Warning(new()), Is.Null, "off: nothing to warn of");
    }

    [Test]
    public void WithNetworkBansOff_NothingIsHashed()
    {
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.1");

        Assert.That(ClientAddresses.Off.HashOf(http), Is.Null);
        Assert.That(Addresses.HashOf(new DefaultHttpContext()), Is.Null, "no address: nothing to hash");
    }

    // The proxy in front, as trusted in production: a network of its own (here a documentation range), which has resolved
    // the client into X-Real-IP.
    private static readonly Dictionary<string, string?> BehindTheProxy = new()
    {
        ["ForwardedHeaders:KnownIPNetworks:0"] = "192.0.2.0/24",
        ["ForwardedHeaders:ForwardedForHeaderName"] = "X-Real-IP",
    };

    private static async Task<string?> HashSeenAsync(string peer, string? realIp, string? forwardedFor)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(BehindTheProxy);
        builder.Services.ConfigureAppForwardedHeaders(builder.Configuration);
        await using var app = builder.Build();
        app.UseForwardedHeaders();
        app.Run(context => context.Response.WriteAsync(Addresses.HashOf(context) ?? ""));
        await app.StartAsync();

        var context = await app.GetTestServer().SendAsync(c =>
        {
            c.Connection.RemoteIpAddress = IPAddress.Parse(peer);
            if (realIp is not null)
            {
                c.Request.Headers["X-Real-IP"] = realIp;
            }

            if (forwardedFor is not null)
            {
                c.Request.Headers["X-Forwarded-For"] = forwardedFor;
            }
        });
        using var reader = new StreamReader(context.Response.Body);
        return await reader.ReadToEndAsync();
    }

    [Test]
    public async Task TheNetwork_IsTheOneTheTrustedProxyResolved()
    {
        Assert.That(await HashSeenAsync("192.0.2.10", realIp: "203.0.113.5", forwardedFor: null), Is.EqualTo(Hash("203.0.113.5")));
    }

    [Test]
    public async Task AForwardedHeaderFromAnyoneElse_IsIgnored()
    {
        // A browser straight at the server (not through the proxy) says it is someone else: it is still itself.
        var spoofed = await HashSeenAsync("198.51.100.7", realIp: "203.0.113.5", forwardedFor: "203.0.113.6");

        Assert.That(spoofed, Is.EqualTo(Hash("198.51.100.7")));
    }
}

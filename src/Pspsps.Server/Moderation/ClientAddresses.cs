using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Pspsps.Server.Moderation;

/// <summary>
/// The network a request comes from, as the server keeps it: a keyed hash (HMAC-SHA256 with the server's secret
/// <c>Moderation:AddressKey</c>), never the address. An IPv6 address counts by its /64 (one household or phone gets a
/// whole /64, and picks a new address in it at will); an IPv4 address by itself. The address is the one ASP.NET Core
/// resolved (<see cref="ConnectionInfo.RemoteIpAddress"/>, after <c>UseForwardedHeaders</c>, which applies a forwarded
/// header only from the proxies the server trusts): never a header a browser sets.
/// </summary>
public sealed class ClientAddresses
{
    /// <summary>The secret key's configuration name.</summary>
    public const string KeyName = "Moderation:AddressKey";

    /// <summary>Whether network bans are on: unset is on exactly when the key is set.</summary>
    public const string EnabledName = "Moderation:AddressBans:Enabled";

    /// <summary>The shortest key taken: a short one could be guessed, and every address with it.</summary>
    public const int MinKeyLength = 32;

    private readonly byte[]? _key;

    private ClientAddresses(byte[]? key)
    {
        _key = key;
    }

    /// <summary>Network bans off: no network is hashed, kept or refused.</summary>
    public static ClientAddresses Off { get; } = new(null);

    /// <summary>Whether network bans are on.</summary>
    public bool Enabled => _key is not null;

    /// <summary>On, with <paramref name="key"/> (at least <see cref="MinKeyLength"/> characters).</summary>
    public static ClientAddresses WithKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return key.Trim().Length < MinKeyLength
            ? throw new InvalidOperationException($"{KeyName} must be at least {MinKeyLength} characters.")
            : new ClientAddresses(Encoding.UTF8.GetBytes(key.Trim()));
    }

    /// <summary>
    /// From the configuration: on with <see cref="KeyName"/> set, unless <see cref="EnabledName"/> is false. Refuses to
    /// start the server with network bans asked for (<see cref="EnabledName"/> true) and no key, or with a short key.
    /// </summary>
    public static ClientAddresses FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var asked = configuration[EnabledName];
        bool? enabled = string.IsNullOrWhiteSpace(asked)
            ? null
            : bool.TryParse(asked, out var on)
                ? on
                : throw new InvalidOperationException($"{EnabledName} is '{asked}': true, false, or unset.");
        var key = configuration[KeyName];
        if (enabled == false)
        {
            return Off;
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            return enabled == true
                ? throw new InvalidOperationException(
                    $"{EnabledName} is true but {KeyName} isn't set: network bans hash addresses with that secret key.")
                : Off;
        }

        return WithKey(key);
    }

    /// <summary>The network of <paramref name="address"/>: an IPv4 address itself (also written as IPv6), an IPv6 one's /64.</summary>
    public static IPAddress NetworkOf(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.IsIPv4MappedToIPv6)
        {
            return address.MapToIPv4();
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return address;
        }

        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return new IPAddress(bytes);
    }

    /// <summary>The network's keyed hash: 64 lowercase hex digits, the same for every address of the network.</summary>
    public string HashOf(IPAddress address)
    {
        var key = _key ?? throw new InvalidOperationException("Network bans are off.");
        return Convert.ToHexStringLower(HMACSHA256.HashData(key, NetworkOf(address).GetAddressBytes()));
    }

    /// <summary>The hash of the network the request came from; null when network bans are off or there's no address.</summary>
    public string? HashOf(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        return Enabled && http.Connection.RemoteIpAddress is { } address ? HashOf(address) : null;
    }
}

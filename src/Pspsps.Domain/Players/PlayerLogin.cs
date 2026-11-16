namespace Pspsps.Domain.Players;

/// <summary>An external account (Discord, Google) a player signs in with. Part of the <see cref="Player"/> aggregate.</summary>
public sealed class PlayerLogin
{
    private PlayerLogin()
    {
        ProviderKey = null!;
    }

    internal PlayerLogin(LoginProvider provider, string providerKey)
    {
        if (string.IsNullOrWhiteSpace(providerKey))
        {
            throw new DomainException("A login needs the provider's user ID.");
        }

        Provider = provider;
        ProviderKey = providerKey;
    }

    public LoginProvider Provider { get; private set; }

    /// <summary>The provider's stable user ID (Discord snowflake, Google subject).</summary>
    public string ProviderKey { get; private set; }
}

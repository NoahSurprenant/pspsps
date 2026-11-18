using Microsoft.Extensions.Configuration;

namespace Pspsps.Integration.Test;

/// <summary>
/// The server's admin list (Moderation:AdminPlayerIds) as configuration a test adds to: as a reloaded configuration file
/// would, it tells the server it changed, and the server reads the list again.
/// </summary>
public sealed class TestAdmins : IConfigurationSource
{
    private readonly Provider _provider = new();

    public IConfigurationProvider Build(IConfigurationBuilder builder) => _provider;

    /// <summary>Makes <paramref name="playerId"/> an admin, from now on.</summary>
    public void Add(Guid playerId) => _provider.Add(playerId);

    private sealed class Provider : ConfigurationProvider
    {
        private readonly Lock _adding = new();
        private int _count;

        public void Add(Guid playerId)
        {
            lock (_adding)
            {
                Set($"Moderation:AdminPlayerIds:{_count++}", playerId.ToString());
            }

            OnReload();
        }
    }
}

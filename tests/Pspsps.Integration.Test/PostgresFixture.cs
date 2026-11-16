using Testcontainers.PostgreSql;

namespace Pspsps.Integration.Test;

/// <summary>
/// One PostgreSQL container and one game server for the whole run: a second server in the process would start a second
/// Orleans silo on the same ports.
/// </summary>
[SetUpFixture]
public sealed class PostgresFixture
{
    private static PostgreSqlContainer? _container;
    private static GameFactory? _game;

    public static GameFactory Game => _game ?? throw new InvalidOperationException("The fixture hasn't started.");

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        _container = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await _container.StartAsync();
        _game = new GameFactory(_container.GetConnectionString());
    }

    [OneTimeTearDown]
    public async Task StopAsync()
    {
        if (_game is not null)
        {
            await _game.DisposeAsync();
        }

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}

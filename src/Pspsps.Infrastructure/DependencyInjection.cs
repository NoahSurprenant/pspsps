using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pspsps.Application.Abstractions;
using Pspsps.Infrastructure.Persistence;

namespace Pspsps.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        // A short connect timeout unless the connection string sets one (PostgresConnectionStrings says why).
        var database = PostgresConnectionStrings.WithConnectDefaults(connectionString);
        services.AddDbContext<PspspsDbContext>(options => options
            // Retries transient failures (a dropped connection, PostgreSQL restarting) in every query, SaveChanges and
            // the startup migration, instead of failing them. About 45 s of retries is too short for a new pod's first
            // connections, which can be refused or dropped while its network policy is applied, so the server first
            // waits for the database (Pspsps.Server's DatabaseStartup).
            .UseNpgsql(database, npgsql => npgsql.EnableRetryOnFailure(
                maxRetryCount: 6, maxRetryDelay: TimeSpan.FromSeconds(10), errorCodesToAdd: null))
            .UseSnakeCaseNamingConvention());
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<PspspsDbContext>());
        services.AddDataProtection()
            .SetApplicationName("pspsps")
            .PersistKeysToDbContext<PspspsDbContext>();
        return services;
    }
}

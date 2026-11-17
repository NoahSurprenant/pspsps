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
        services.AddDbContext<PspspsDbContext>(options => options
            // Retries transient failures (a refused connection while a new pod's network policy applies, PostgreSQL
            // restarting) in every query, SaveChanges and the startup migration, instead of failing them.
            .UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(
                maxRetryCount: 6, maxRetryDelay: TimeSpan.FromSeconds(10), errorCodesToAdd: null))
            .UseSnakeCaseNamingConvention());
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<PspspsDbContext>());
        services.AddDataProtection()
            .SetApplicationName("pspsps")
            .PersistKeysToDbContext<PspspsDbContext>();
        return services;
    }
}

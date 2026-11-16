using Microsoft.Extensions.DependencyInjection;
using Pspsps.Application.Players;

namespace Pspsps.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Random.Shared);
        services.AddScoped<IPlayerService, PlayerService>();
        return services;
    }
}

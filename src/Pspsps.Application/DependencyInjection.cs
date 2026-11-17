using Microsoft.Extensions.DependencyInjection;
using Pspsps.Application.Friends;
using Pspsps.Application.Players;
using Pspsps.Application.Shop;

namespace Pspsps.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Random.Shared);
        services.AddScoped<IPlayerService, PlayerService>();
        services.AddScoped<IFriendService, FriendService>();
        services.AddScoped<IShopService, ShopService>();
        return services;
    }
}

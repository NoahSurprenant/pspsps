using Microsoft.Extensions.DependencyInjection;
using Pspsps.Application.Friends;
using Pspsps.Application.Homes;
using Pspsps.Application.Moderation;
using Pspsps.Application.Players;
using Pspsps.Application.Safety;
using Pspsps.Application.Shop;

namespace Pspsps.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Random.Shared);
        // Bans and mutes are timed by the system's clock, unless a test swaps this one for its own.
        services.AddKeyedSingleton(ModerationClock.Key, (provider, _) => provider.GetRequiredService<TimeProvider>());
        services.AddScoped<IPlayerService, PlayerService>();
        services.AddScoped<IFriendService, FriendService>();
        services.AddScoped<IShopService, ShopService>();
        services.AddScoped<IHomeService, HomeService>();
        services.AddScoped<IModerationService, ModerationService>();
        services.AddScoped<IAddressBanService, AddressBanService>();
        services.AddScoped<IMuteService, MuteService>();
        services.AddScoped<IReportService, ReportService>();
        return services;
    }
}

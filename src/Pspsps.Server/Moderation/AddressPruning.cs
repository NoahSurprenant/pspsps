using Pspsps.Application.Moderation;

namespace Pspsps.Server.Moderation;

/// <summary>
/// Forgets, every <see cref="Every"/> (and once as the server starts), the networks players haven't been seen on for
/// 7 days and the networks' bans that are over (<see cref="IAddressBanService.PruneAsync"/>). With network bans off, every
/// network and ban kept: turning them off forgets them all within the hour. Every pod does it; a second run finds nothing.
/// </summary>
public sealed partial class AddressPruning(
    IServiceScopeFactory scopes, ClientAddresses addresses, TimeProvider time, ILogger<AddressPruning> logger)
    : BackgroundService
{
    public static readonly TimeSpan Every = TimeSpan.FromHours(1);

    /// <summary>One pruning: never throws (the next one tries again).</summary>
    public async Task PruneAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var pruned = await scope.ServiceProvider.GetRequiredService<IAddressBanService>()
                .PruneAsync(forgetAll: !addresses.Enabled, cancellationToken);
            if (pruned > 0)
            {
                LogPruned(logger, pruned);
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            LogNotPruned(logger, e);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Every, time);
        try
        {
            do
            {
                await PruneAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Stopping.
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Forgot {Count} old networks and ended network bans")]
    private static partial void LogPruned(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't forget old networks; trying again later")]
    private static partial void LogNotPruned(ILogger logger, Exception exception);
}

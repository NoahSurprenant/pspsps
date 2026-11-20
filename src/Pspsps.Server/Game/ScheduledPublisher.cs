using Pspsps.Application.Rooms;

namespace Pspsps.Server.Game;

/// <summary>Publishes the room drafts whose go-live time has come. Pods that find the same drafts due race harmlessly: one publishes, the others see it done.</summary>
internal sealed partial class ScheduledPublisher(IServiceScopeFactory scopes, ILogger<ScheduledPublisher> logger) : BackgroundService
{
    internal static readonly TimeSpan Every = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Every);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var published = await scope.ServiceProvider.GetRequiredService<IRoomEditor>().PublishDueAsync(stoppingToken);
                if (published > 0)
                {
                    LogPublished(published);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogFailed(exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Scheduled room drafts went live: {Count}.")]
    private partial void LogPublished(int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Scheduled room drafts could not be published; trying again.")]
    private partial void LogFailed(Exception exception);
}

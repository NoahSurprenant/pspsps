using Pspsps.Application.Rooms;

namespace Pspsps.Server.Game;

/// <summary>Reads the published rooms and overlays again every <see cref="Every"/>, so a room published on one pod reaches the others.</summary>
internal sealed partial class RoomLibraryRefresher(IRoomLibrary library, ILogger<RoomLibraryRefresher> logger) : BackgroundService
{
    internal static readonly TimeSpan Every = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Every);
        do
        {
            try
            {
                await library.RefreshAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogRefreshFailed(exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The rooms could not be read again; keeping the ones in use.")]
    private partial void LogRefreshFailed(Exception exception);
}

using System.Threading.Channels;

namespace Pspsps.Server.Telemetry.Browser;

/// <summary>
/// The browsers' batches waiting for the collector: bounded, so a collector that is slow or away costs memory up to
/// <see cref="Capacity"/> batches, and drops the newest past that rather than hold requests open.
/// </summary>
public sealed partial class ClientTelemetryOutbox(ILogger<ClientTelemetryOutbox> logger, ForwardFailures failures)
{
    public const int Capacity = 1_000;

    private readonly Channel<(ClientTelemetryBatch Batch, string UserAgent)> _channel =
        // Wait, not a Drop mode: those report every write as done, so a full outbox couldn't be told (and warned of).
        Channel.CreateBounded<(ClientTelemetryBatch, string)>(new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
        });

    /// <summary>Queues a batch with spans or logs to forward; a full outbox drops it (warned of at most once a minute).</summary>
    public bool Enqueue(ClientTelemetryBatch batch, string userAgent)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (batch.Spans.Count == 0 && batch.Logs.Count == 0)
            return false;
        if (_channel.Writer.TryWrite((batch, userAgent)))
            return true;
        if (failures.ShouldLog(out var suppressed))
            LogFull(logger, Capacity, suppressed);
        return false;
    }

    /// <summary>Forwards every queued batch as it comes, until <paramref name="ct"/> (the host stopping).</summary>
    public async Task ForwardAsync(Func<ClientTelemetryForwarder> forwarder, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(forwarder);
        await foreach (var (batch, userAgent) in _channel.Reader.ReadAllAsync(ct))
        {
            try
            {
                await forwarder().ForwardAsync(batch, userAgent, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // One batch that couldn't be sent (or built) mustn't stop the rest.
                if (failures.ShouldLog(out var suppressed))
                    LogForwardFailed(logger, e, suppressed);
            }
        }
    }

    /// <summary>Forwards what is queued now, and returns: for tests.</summary>
    public async Task ForwardQueuedAsync(ClientTelemetryForwarder forwarder, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(forwarder);
        while (_channel.Reader.TryRead(out var item))
            await forwarder.ForwardAsync(item.Batch, item.UserAgent, ct);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The browsers' telemetry outbox is full ({Capacity} batches): dropping batches; {Suppressed} more failures since the last warning")]
    private static partial void LogFull(ILogger logger, int capacity, int suppressed);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A browser's telemetry batch couldn't be forwarded; {Suppressed} more failures since the last warning")]
    private static partial void LogForwardFailed(ILogger logger, Exception e, int suppressed);
}

/// <summary>Forwards the queued batches to the collector, in the background, for the host's life.</summary>
public sealed class ClientTelemetryPump(ClientTelemetryOutbox outbox, IServiceProvider services) : BackgroundService
{
    // A forwarder for each batch: it's a typed HttpClient, which IHttpClientFactory hands out per use.
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        outbox.ForwardAsync(services.GetRequiredService<ClientTelemetryForwarder>, stoppingToken);
}

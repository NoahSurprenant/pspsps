using System.Threading.Channels;

namespace Pspsps.Server.Telemetry.Browser;

/// <summary>A browser's batch waiting for the collector, with the user agent that sent it.</summary>
public sealed record QueuedBatch(ClientTelemetryBatch Batch, string UserAgent);

/// <summary>
/// The browsers' batches waiting for the collector: bounded, so a collector that is slow or away costs memory up to
/// <see cref="Capacity"/> batches, and drops the newest past that rather than hold requests open. Forwarded up to
/// <see cref="MaxBatchesPerPost"/> at a time, in one post a signal.
/// </summary>
public sealed partial class ClientTelemetryOutbox(ILogger<ClientTelemetryOutbox> logger, ForwardFailures failures)
{
    public const int Capacity = 1_000;

    /// <summary>The most batches one post carries: about 2000 spans and 2000 lines at the most, a few MB.</summary>
    public const int MaxBatchesPerPost = 20;

    private readonly ForwardFailures _forwardFailures = failures;

    // Its own once-a-minute warning: in a collector outage the forward failures take theirs, and a full outbox must
    // still be told.
    private readonly ForwardFailures _full = failures.Separate();

    private readonly ILogger<ClientTelemetryOutbox> _logger = logger;

    private readonly Channel<QueuedBatch> _channel =
        // Wait, not a Drop mode: those report every write as done, so a full outbox couldn't be told (and warned of).
        Channel.CreateBounded<QueuedBatch>(new BoundedChannelOptions(Capacity)
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
        if (_channel.Writer.TryWrite(new QueuedBatch(batch, userAgent)))
            return true;
        if (_full.ShouldLog(out var suppressed))
            LogFull(_logger, Capacity, suppressed);
        return false;
    }

    /// <summary>Forwards what is queued as it comes, until <paramref name="ct"/> (the host stopping).</summary>
    public async Task ForwardAsync(Func<ClientTelemetryForwarder> forwarder, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(forwarder);
        while (await _channel.Reader.WaitToReadAsync(ct))
            await ForwardSomeAsync(forwarder, ct);
    }

    /// <summary>Forwards what is queued now, the same way, and returns: for tests.</summary>
    internal async Task ForwardQueuedAsync(ClientTelemetryForwarder forwarder, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(forwarder);
        while (_channel.Reader.TryPeek(out _))
            await ForwardSomeAsync(() => forwarder, ct);
    }

    /// <summary>Up to <see cref="MaxBatchesPerPost"/> of what is queued, in one forward.</summary>
    private async Task ForwardSomeAsync(Func<ClientTelemetryForwarder> forwarder, CancellationToken ct)
    {
        var batches = new List<QueuedBatch>();
        while (batches.Count < MaxBatchesPerPost && _channel.Reader.TryRead(out var queued))
            batches.Add(queued);
        if (batches.Count == 0)
            return;
        try
        {
            await forwarder().ForwardAsync(batches, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Those batches couldn't be sent (or built): the rest still go.
            if (_forwardFailures.ShouldLog(out var suppressed))
                LogForwardFailed(_logger, e, suppressed);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The browsers' telemetry outbox is full ({Capacity} batches): dropping batches; {Suppressed} more failures since the last warning")]
    private static partial void LogFull(ILogger logger, int capacity, int suppressed);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Browsers' telemetry batches couldn't be forwarded; {Suppressed} more failures since the last warning")]
    private static partial void LogForwardFailed(ILogger logger, Exception e, int suppressed);
}

/// <summary>Forwards the queued batches to the collector, in the background, for the host's life.</summary>
public sealed class ClientTelemetryPump(ClientTelemetryOutbox outbox, IServiceProvider services) : BackgroundService
{
    // A forwarder for each post: it's a typed HttpClient, which IHttpClientFactory hands out per use.
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        outbox.ForwardAsync(services.GetRequiredService<ClientTelemetryForwarder>, stoppingToken);
}

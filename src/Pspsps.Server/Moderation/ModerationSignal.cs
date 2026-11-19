using System.Collections.Concurrent;
using StackExchange.Redis;

namespace Pspsps.Server.Moderation;

/// <summary>
/// What a moderation changes on every pod, not only on the one that decided it: each pod holds its own players'
/// connections and its own read of their standing (<see cref="StandingCache"/>).
/// </summary>
public interface IModerationSignal
{
    /// <summary>
    /// The player's standing changed: every other pod reads it again at the player's next call, and with
    /// <paramref name="close"/> (a ban) every pod closes the player's connections it holds, after
    /// <see cref="ModerationOnThisPod.CloseAfter"/>. Never throws: what can't reach the other pods still applies here.
    /// </summary>
    Task SendAsync(Guid playerId, bool close);
}

/// <summary>What a moderation signal does on this pod.</summary>
public sealed class ModerationOnThisPod(PlayerConnections connections, StandingCache standings, TimeProvider time)
{
    /// <summary>How long a banned player's tab has to hear why before its connection is closed.</summary>
    public static readonly TimeSpan CloseAfter = TimeSpan.FromSeconds(2);

    // The timers that will close banned players' connections: kept, so they aren't collected before they fire.
    private readonly ConcurrentDictionary<ITimer, byte> _closing = new();

    /// <summary>
    /// A signal for <paramref name="playerId"/>: decided on another pod, this pod forgets what it knew of their standing;
    /// with <paramref name="close"/>, their connections on this pod close after <see cref="CloseAfter"/>.
    /// </summary>
    public void Apply(Guid playerId, bool close, bool decidedHere)
    {
        if (!decidedHere)
        {
            standings.Forget(playerId);
        }

        if (close)
        {
            CloseLater(playerId);
        }
    }

    // Closes the player's connections on this pod after CloseAfter, once their tabs have had time to hear why.
    private void CloseLater(Guid playerId)
    {
        var open = connections.Of(playerId);
        if (open.Count == 0)
        {
            return;
        }

        ITimer? timer = null;
        timer = time.CreateTimer(state =>
        {
            foreach (var connection in open)
            {
                connection.Abort();
            }

            if (timer is not null && _closing.TryRemove(timer, out _))
            {
                timer.Dispose();
            }
        }, null, CloseAfter, Timeout.InfiniteTimeSpan);
        _closing[timer] = 0;
    }
}

/// <summary>One pod, no Redis: the signal applies here, where everything is.</summary>
internal sealed class LocalModerationSignal(ModerationOnThisPod pod) : IModerationSignal
{
    public Task SendAsync(Guid playerId, bool close)
    {
        pod.Apply(playerId, close, decidedHere: true);
        return Task.CompletedTask;
    }
}

/// <summary>
/// A moderation signal as it travels between pods: <c>1|sender|player|close</c>. A newer pod may add fields after these;
/// an older one reads the four it knows and ignores the rest (docs/architecture.md, "Changing what pods say to each
/// other").
/// </summary>
public sealed record ModerationMessage(string Sender, Guid PlayerId, bool Close)
{
    private const string Version = "1";

    public string Format() =>
        string.Join('|', Version, Sender, PlayerId.ToString(), Close ? "1" : "0");

    /// <summary>The message in <paramref name="text"/>, or null when it isn't one.</summary>
    public static ModerationMessage? Parse(string? text)
    {
        var parts = text?.Split('|');
        if (parts is not { Length: >= 4 } || parts[0] != Version || parts[1].Length == 0
            || !Guid.TryParseExact(parts[2], "D", out var playerId) || parts[3] is not ("0" or "1"))
        {
            return null;
        }

        return new ModerationMessage(parts[1], playerId, parts[3] == "1");
    }
}

/// <summary>
/// Several pods: the signal goes through Redis pub/sub (<see cref="Channel"/>), which every pod subscribes to, this one
/// included, so every pod applies it the same way. Pub/sub delivers to whoever is subscribed then, at most once: a pod
/// cut off from Redis at that moment misses it, and falls back on <see cref="StandingCache.KeepFor"/> (its next read of
/// the player's standing refuses their calls, and the refusal closes the connection).
/// </summary>
internal sealed partial class RedisModerationSignal(
    string configuration, ModerationOnThisPod pod, ILogger<RedisModerationSignal> logger)
    : IModerationSignal, IHostedService, IAsyncDisposable
{
    public static readonly RedisChannel Channel = RedisChannel.Literal("pspsps:moderation");

    // Which pod sent a message: applied here already, right after SendAsync publishes it, so its own coming back
    // through the subscription (Receive) is ignored instead of applied a second time.
    private readonly string _self = Guid.NewGuid().ToString("N");
    private ConnectionMultiplexer? _redis;

    /// <summary>This pod's sender id, for tests: what a message this pod sent carries, so a test can address one back to it.</summary>
    internal string SelfId => _self;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var options = ConfigurationOptions.Parse(configuration);
        // Redis must answer at startup (Orleans' membership table needs it then too). Once connected, the multiplexer
        // reconnects by itself and subscribes again if Redis restarts; signals published meanwhile are missed, and
        // StandingCache.KeepFor covers them.
        options.AbortOnConnectFail = false;
        _redis = await ConnectionMultiplexer.ConnectAsync(options);
        await _redis.GetSubscriber().SubscribeAsync(Channel, (_, value) => Receive(value));
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task SendAsync(Guid playerId, bool close)
    {
        try
        {
            var redis = _redis ?? throw new InvalidOperationException("Not connected to Redis yet.");
            await redis.GetSubscriber().PublishAsync(Channel, new ModerationMessage(_self, playerId, close).Format());
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            LogNotSent(logger, e, playerId);
        }

        // Applied here regardless of whether the publish reached Redis, not waited for the pod's own message to come
        // back through its subscription (Receive ignores it: see _self above). Waiting on that round trip was the only
        // way a banning pod closed a banned player's own connections it holds, so a slow or unreachable Redis delayed
        // or dropped that close.
        pod.Apply(playerId, close, decidedHere: true);
    }

    public async ValueTask DisposeAsync()
    {
        if (_redis is not null)
        {
            await _redis.DisposeAsync();
        }
    }

    /// <summary>
    /// A message off the subscription: applied here, unless this pod sent it (<see cref="SendAsync"/> already applied
    /// it, right after publishing, without waiting for this).
    /// </summary>
    internal void Receive(RedisValue value)
    {
        if (ModerationMessage.Parse(value) is not { } message)
        {
            LogUnreadable(logger, value.ToString());
            return;
        }

        if (message.Sender == _self)
        {
            return;
        }

        pod.Apply(message.PlayerId, message.Close, decidedHere: false);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't tell the other pods that player {PlayerId}'s standing changed; they read it again within the standing cache's time")]
    private static partial void LogNotSent(ILogger logger, Exception exception, Guid playerId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignored a moderation signal this pod can't read: {Message}")]
    private static partial void LogUnreadable(ILogger logger, string message);
}

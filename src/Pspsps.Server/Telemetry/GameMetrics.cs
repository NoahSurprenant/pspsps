using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.SignalR;

namespace Pspsps.Server.Telemetry;

/// <summary>
/// The game's own metrics. Counts are per pod; sum them across pods (each pod counts the connections and rooms it
/// hosts).
/// </summary>
public sealed class GameMetrics
{
    public const string MeterName = "Pspsps.Game";

    private readonly UpDownCounter<long> _worldConnections;
    private readonly UpDownCounter<long> _activeRooms;
    private readonly Counter<long> _chatLines;
    private readonly Counter<long> _expiredLeases;
    private readonly Counter<long> _emotes;
    private readonly Histogram<double> _hubCallDuration;

    public GameMetrics(IMeterFactory meters)
    {
        var meter = meters.Create(MeterName);
        _worldConnections = meter.CreateUpDownCounter<long>("pspsps.world.connections", "{connection}",
            "Browser tabs in a world, by world.");
        _activeRooms = meter.CreateUpDownCounter<long>("pspsps.rooms.active", "{room}",
            "Rooms with at least one cat, hosted on this pod.");
        _chatLines = meter.CreateCounter<long>("pspsps.chat.lines", "{line}", "Chat lines said, by world.");
        _expiredLeases = meter.CreateCounter<long>("pspsps.leases.expired", "{lease}",
            "Leases that ran out because nothing renewed them (their pod died), by scope: room (a cat) or world (a tab).");
        _emotes = meter.CreateCounter<long>("pspsps.emotes", "{emote}", "Emotes done, by world and emote.");
        _hubCallDuration = meter.CreateHistogram<double>("pspsps.hub.call.duration", "s",
            "How long hub method calls take, by method and outcome.",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = [0.001, 0.0025, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5] });
    }

    public void EnteredWorld(string worldId) => _worldConnections.Add(1, new KeyValuePair<string, object?>("world", worldId));

    public void LeftWorld(string worldId) => _worldConnections.Add(-1, new KeyValuePair<string, object?>("world", worldId));

    public void RoomActivated() => _activeRooms.Add(1);

    public void RoomDeactivated() => _activeRooms.Add(-1);

    /// <summary>Counts <paramref name="count"/> leases that ran out in a <paramref name="scope"/>: "room" or "world".</summary>
    public void LeasesExpired(string scope, int count) =>
        _expiredLeases.Add(count, new KeyValuePair<string, object?>("scope", scope));

    public void Said(string worldId) => _chatLines.Add(1, new KeyValuePair<string, object?>("world", worldId));

    public void Emoted(string worldId, string emote) =>
        _emotes.Add(1, new KeyValuePair<string, object?>("world", worldId), new KeyValuePair<string, object?>("emote", emote));

    public void HubCall(string method, string outcome, TimeSpan duration) =>
        _hubCallDuration.Record(duration.TotalSeconds,
            new KeyValuePair<string, object?>("method", method), new KeyValuePair<string, object?>("outcome", outcome));
}

/// <summary>
/// Times every hub method call into <see cref="GameMetrics"/>: outcome <c>ok</c>, <c>refused</c> (a game rule said
/// no; the player sees the message) or <c>error</c>.
/// </summary>
public sealed class HubCallMetricsFilter(GameMetrics metrics) : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var started = Stopwatch.GetTimestamp();
        var outcome = "error";
        try
        {
            var result = await next(invocationContext);
            outcome = "ok";
            return result;
        }
        catch (HubException)
        {
            outcome = "refused";
            throw;
        }
        finally
        {
            metrics.HubCall(invocationContext.HubMethodName, outcome, Stopwatch.GetElapsedTime(started));
        }
    }
}

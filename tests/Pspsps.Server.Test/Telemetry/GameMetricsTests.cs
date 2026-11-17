using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Pspsps.Server.Telemetry;

namespace Pspsps.Server.Test.Telemetry;

public sealed class GameMetricsTests
{
    private ServiceProvider _services = null!;
    private MeterListener _listener = null!;
    private List<(string Instrument, double Value, Dictionary<string, object?> Tags)> _measurements = null!;

    [SetUp]
    public void Listen()
    {
        _services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        _measurements = [];
        _listener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == GameMetrics.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            },
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument, value, tags));
        _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument, value, tags));
        _listener.Start();
    }

    [TearDown]
    public void Stop()
    {
        _listener.Dispose();
        _services.Dispose();
    }

    [Test]
    public void WorldConnections_GoUpAndDownByWorld()
    {
        var metrics = new GameMetrics(_services.GetRequiredService<IMeterFactory>());

        metrics.EnteredWorld("tabby");
        metrics.LeftWorld("tabby");

        Assert.That(_measurements.Select(m => (m.Instrument, m.Value, m.Tags["world"])), Is.EqualTo(new[]
        {
            ("pspsps.world.connections", 1.0, (object?)"tabby"),
            ("pspsps.world.connections", -1.0, (object?)"tabby"),
        }));
    }

    [Test]
    public void RoomsAndChat_AreCounted()
    {
        var metrics = new GameMetrics(_services.GetRequiredService<IMeterFactory>());

        metrics.RoomActivated();
        metrics.Said("calico");
        metrics.RoomDeactivated();

        Assert.That(_measurements.Select(m => (m.Instrument, m.Value)), Is.EqualTo(new[]
        {
            ("pspsps.rooms.active", 1.0),
            ("pspsps.chat.lines", 1.0),
            ("pspsps.rooms.active", -1.0),
        }));
    }

    [Test]
    public void Emotes_AreCountedByWorldAndEmote()
    {
        var metrics = new GameMetrics(_services.GetRequiredService<IMeterFactory>());

        metrics.Emoted("calico", "wave");
        metrics.Emoted("tabby", "sleep");

        Assert.That(_measurements.Select(m => (m.Instrument, m.Value, m.Tags["world"], m.Tags["emote"])), Is.EqualTo(new[]
        {
            ("pspsps.emotes", 1.0, (object?)"calico", (object?)"wave"),
            ("pspsps.emotes", 1.0, (object?)"tabby", (object?)"sleep"),
        }));
    }

    [Test]
    public async Task HubFilter_RecordsEachCallWithItsOutcome()
    {
        var filter = new HubCallMetricsFilter(new GameMetrics(_services.GetRequiredService<IMeterFactory>()));

        await filter.InvokeMethodAsync(Call("Move"), _ => ValueTask.FromResult<object?>(null));
        Assert.ThrowsAsync<HubException>(async () =>
            await filter.InvokeMethodAsync(Call("Say"), _ => throw new HubException("Say something.")));
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await filter.InvokeMethodAsync(Call("GoToRoom"), _ => throw new InvalidOperationException()));

        Assert.That(_measurements.Select(m => (m.Instrument, m.Tags["method"], m.Tags["outcome"])), Is.EqualTo(new[]
        {
            ("pspsps.hub.call.duration", (object?)"Move", (object?)"ok"),
            ("pspsps.hub.call.duration", (object?)"Say", (object?)"refused"),
            ("pspsps.hub.call.duration", (object?)"GoToRoom", (object?)"error"),
        }));
        Assert.That(_measurements.Select(m => m.Value), Is.All.GreaterThanOrEqualTo(0));
    }

    // The filter reads the method's name from the hub method itself, so these are GameHub's real ones.
    private static HubInvocationContext Call(string method) =>
        new(Substitute.For<HubCallerContext>(), Substitute.For<IServiceProvider>(), Substitute.For<Hub>(),
            typeof(Pspsps.Server.Hubs.GameHub).GetMethod(method)!, []);

    private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var dictionary = new Dictionary<string, object?>();
        foreach (var tag in tags)
        {
            dictionary[tag.Key] = tag.Value;
        }

        _measurements.Add((instrument.Name, value, dictionary));
    }
}

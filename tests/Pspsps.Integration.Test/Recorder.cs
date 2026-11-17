using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR.Client;
using Pspsps.Server.Game;

namespace Pspsps.Integration.Test;

/// <summary>Everything the server sends one connection, read in order.</summary>
internal sealed class Recorder
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private readonly Channel<(string Method, object Payload)> _events = Channel.CreateUnbounded<(string, object)>();

    public Recorder(HubConnection hub)
    {
        hub.On<CatView>("CatJoined", c => _events.Writer.TryWrite(("CatJoined", c)));
        hub.On<Guid>("CatLeft", id => _events.Writer.TryWrite(("CatLeft", id)));
        hub.On<Guid, double, double>("CatMoved", (id, x, z) => _events.Writer.TryWrite(("CatMoved", (id, x, z))));
        hub.On<ChatView>("ChatSaid", c => _events.Writer.TryWrite(("ChatSaid", c)));
        hub.On<EmoteView>("CatEmoted", e => _events.Writer.TryWrite(("CatEmoted", e)));
    }

    /// <summary>Whether a matching event arrives within <paramref name="within"/>.</summary>
    public async Task<bool> Saw<T>(string method, Func<T, bool> match, TimeSpan within)
    {
        using var timeout = new CancellationTokenSource(within);
        try
        {
            while (true)
            {
                var (name, payload) = await _events.Reader.ReadAsync(timeout.Token);
                if (name == method && payload is T value && match(value))
                {
                    return true;
                }
            }
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public async Task<T> Next<T>(string method, Func<T, bool> match)
    {
        using var timeout = new CancellationTokenSource(Wait);
        while (true)
        {
            var (name, payload) = await _events.Reader.ReadAsync(timeout.Token);
            if (name == method && payload is T value && match(value))
            {
                return value;
            }
        }
    }
}

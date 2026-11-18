using System.Net;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Pspsps.Server.Test.Telemetry.Browser;

/// <summary>An OTLP/HTTP collector: records each post (its path and JSON), and answers <see cref="Status"/>.</summary>
public sealed class FakeCollector : HttpMessageHandler
{
    private readonly Channel<(string Path, JsonNode Body)> _posts = Channel.CreateUnbounded<(string, JsonNode)>();

    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

    /// <summary>Throws this instead of answering, when set.</summary>
    public Exception? Failure { get; set; }

    public List<(string Path, JsonNode Body)> Posts { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Failure is not null)
            throw Failure;
        var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!;
        var post = (request.RequestUri!.AbsolutePath, body);
        lock (Posts)
            Posts.Add(post);
        await _posts.Writer.WriteAsync(post, cancellationToken);
        return new HttpResponseMessage(Status);
    }

    /// <summary>The next post, within a few seconds.</summary>
    public async Task<(string Path, JsonNode Body)> NextAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        return await _posts.Reader.ReadAsync(timeout.Token);
    }
}

/// <summary>A logger that keeps what is logged.</summary>
public sealed class RecordingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Lines { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (Lines)
            Lines.Add((logLevel, formatter(state, exception)));
    }
}

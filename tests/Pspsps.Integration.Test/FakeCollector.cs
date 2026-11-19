using System.Net;
using System.Text.Json.Nodes;

namespace Pspsps.Integration.Test;

/// <summary>
/// The OTLP/HTTP collector the server forwards the browsers' telemetry to (GameFactory points ClientTelemetry at it):
/// keeps every post, its path and JSON.
/// </summary>
public sealed class FakeCollector : HttpMessageHandler
{
    private readonly List<(string Path, JsonNode Body)> _posts = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!;
        lock (_posts)
            _posts.Add((request.RequestUri!.AbsolutePath, body));
        return new HttpResponseMessage(HttpStatusCode.OK);
    }

    /// <summary>The first post to <paramref name="path"/> whose JSON has <paramref name="text"/> in it, within a few seconds.</summary>
    public async Task<JsonNode> PostWith(string path, string text)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < until)
        {
            lock (_posts)
            {
                foreach (var (postPath, body) in _posts)
                {
                    if (postPath == path && body.ToJsonString().Contains(text, StringComparison.Ordinal))
                        return body;
                }
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"Nothing posted to {path} with {text}");
    }
}

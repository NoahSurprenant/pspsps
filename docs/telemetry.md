# Telemetry

Logs, metrics and traces go out over OTLP to a collector when `OTEL_EXPORTER_OTLP_ENDPOINT` is set; nothing is
scraped from the server. Without an endpoint (development, the tests) logs still go to the console as compact JSON.
The code is in `src/Pspsps.Server/Telemetry/`.

## Settings

Standard OpenTelemetry environment variables (the chart sets them from `telemetry.*`):

| Variable | Meaning |
| --- | --- |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | The collector, e.g. `http://otel-collector:4317`. Unset: no export |
| `OTEL_EXPORTER_OTLP_PROTOCOL` | `grpc` (default, 4317) or `http/protobuf` (4318) |
| `OTEL_SERVICE_NAME` | `service.name` on every signal |
| `OTEL_RESOURCE_ATTRIBUTES` | Extra resource attributes; `deployment.environment.name=prod` goes here |
| `OTEL_TRACES_SAMPLER_ARG` | Share of root spans kept, 0 to 1, default 0.1 |
| `OTEL_METRIC_EXPORT_INTERVAL` | Milliseconds between metric pushes, default 30 s |

`service.instance.id` is the pod name and `service.version` the commit the image was built from.

## Logs

Serilog: compact JSON on the console, plus OTLP when an endpoint is set. Each event carries its trace and span ID.
One line per HTTP request, with the client address, host, user agent and the player's ID. Levels are in
`appsettings.json` (`Serilog:MinimumLevel`); EF Core's per-command logs are off (Warning).

## Traces

ASP.NET Core requests, hub method calls (`Microsoft.AspNetCore.SignalR.Server`), grain calls (Orleans, followed
across pods), EF Core and Npgsql, and outgoing HTTP (sign-in providers). SignalR makes every hub call a root span,
so roots are kept at `OTEL_TRACES_SAMPLER_ARG`; database calls that start a trace of their own (background work) are
dropped. Every response has a `traceresponse` header with its trace ID.

## Metrics

The ASP.NET Core, HTTP client, runtime, process, Npgsql, EF Core, health check and Orleans meters, and the game's own
(`Pspsps.Game`), each counted per pod:

| Metric | |
| --- | --- |
| `pspsps.world.connections` | Browser tabs in a world, by `world` |
| `pspsps.rooms.active` | Rooms with a cat in them, hosted here |
| `pspsps.chat.lines` | Chat lines said, by `world` |
| `pspsps.leases.expired` | Leases that ran out because nothing renewed them (their pod died), by `scope`: `room` (a cat, counted where the room is hosted) or `world` (a tab) |
| `pspsps.emotes` | Emotes done, by `world` and `emote` (`sit`, `meow`, `wave`, `sleep`) |
| `pspsps.fish.caught` | Fish caught at a pond, by `world` and `fish` (`boot`, `minnow`, `goldfish`, `trout`, `catfish`, `koi`) |
| `pspsps.hub.call.duration` | Hub method calls, by `method` and `outcome` (`ok`, `refused`, `error`) |

## From the browser

Off unless `ClientTelemetry:Enabled` (the chart's `telemetry.browser`). The players' browsers then send their errors,
spans, connection log lines and performance numbers to `POST /api/client-telemetry`, which forwards the spans and logs
to the collector as OTLP/JSON over HTTP (`ClientTelemetry:OtlpEndpoint`, the collector's OTLP/HTTP base, e.g.
`http://otel-collector:4318`) and records the numbers as metrics. No OpenTelemetry SDK in the browser: a small batcher
(`client/src/telemetry/`). The server side is `src/Pspsps.Server/Telemetry/Browser/`.

| Setting | Meaning |
| --- | --- |
| `ClientTelemetry:Enabled` | On. Off (the default): the endpoint answers 404, which turns the browser's telemetry off for the page |
| `ClientTelemetry:OtlpEndpoint` | The collector's OTLP/HTTP base; required when on (checked at startup) |
| `ClientTelemetry:PermitsPerMinute` | Batches a minute per player (or per client address, signed out), per pod; default 60. A tab sends one every 5 s at most |

### On or off, without a request

When it is on, the server marks the game's page with a `Server-Timing: pspsps-telemetry;desc="<ratio>"` header (any
HTML response, and any answer to a page load). The browser reads it from the navigation's timing
(`performance.getEntriesByType("navigation")[0].serverTiming`), so knowing costs no request. Without the mark the browser
records and sends nothing, and adds no trace headers. It is also off whenever `navigator.webdriver` is set (the e2e tests,
other automation) and under Lighthouse (`Chrome-Lighthouse` in the user agent), and for the rest of the page once the
endpoint answers 404.

### What the browser sends

- **Errors**: uncaught errors and unhandled rejections; calls to the server that got no answer (not the browser's doing:
  a page going away, hidden or offline doesn't count) or a 5xx; the 3D scene's chunks or the cat model failing to load;
  Babylon's own errors and warnings; WebGL context lost (a warning) and restored (info). At most 10 error reports a page,
  each once; the app's own log lines at most 50 of each level, each once.
- **The hub connection**: up, dropped and reconnecting (warning), back, closed (info; a warning when it closed with an
  error), taken over by another tab ("Joined from another tab"), and its calls: refused by the game (info, without the
  server's message, which may name a player), cut off with the connection (warning), failed (error).
- **Spans**: a client span for each call to the server over HTTP, and one for each hub call (`GameHub/<method>`).
- **Numbers** (metrics, over the whole game, with no label a browser sets): `pspsps.browser.lcp`, `.inp`, `.fcp`, `.ttfb`
  (seconds) and `.cls` (web-vitals, loaded only when on); `pspsps.browser.fps`, the median frame rate over ten seconds
  while a room shows; `pspsps.browser.long_task.duration`, long tasks where the browser reports them (at most 30 a page).

Spans and logs go under the game's resource (its `service.name` and environment) with `service.component=browser`, the
browser's own build as `service.version` (the commit the client was built from, `SOURCE_REVISION`), and
`user_agent.original`; never the pod's `service.instance.id`. Every span and line has `session.id`, a random ID for the
page load, to read one page's together.

Never sent: chat, display names, emails, tokens, player IDs. Hub calls' arguments are never recorded. URLs go as path and
query with sensitive query values `REDACTED` (names matching
`token|code|secret|passw|pwd|key|auth|session|sig|credential|cookie|email|user|state|nonce|otp|ticket|hash|jwt`, and
`id`, SignalR's connection token), in messages and stacks too; the server redacts them again, keeps at most 32
attributes of at most 1000 characters each, named like OpenTelemetry's, and none named `service.*`.

The endpoint is anonymous but takes batches from the game's own pages only (`Sec-Fetch-Site`, else `Origin`), at most
256 KiB, and refuses (400) anything the browser couldn't have sent: more than 100 spans, 100 logs or 20 numbers,
malformed IDs, times outside 2001-2286. Batches are queued (at most 1000) for a background forwarder, which posts with a
5 s timeout and warns of failures at most once a minute; the endpoint is left out of the server's traces and request log.

### Hub calls, traced from the browser

The browser puts each hub call's trace context in the call itself: SignalR's hub protocol has headers on every
invocation message (the JavaScript client sets none of its own), and ASP.NET Core SignalR continues a `traceparent`
found there in its hub method span (`Microsoft.AspNetCore.SignalR.Server`), through the grains it calls. The client
wraps SignalR's JSON protocol (`TracingHubProtocol`) to add the header to the one invocation being written:
`HubConnection.invoke` writes its message synchronously, so the header set around the call goes on that call and no
other. Nothing changes on the server, the hub's arguments stay as they are, and a call made without telemetry carries
no header and is traced as before.

The browser samples hub calls itself, at the share in the page's mark (the server's `OTEL_TRACES_SAMPLER_ARG`): a call it
keeps says `01`, one it doesn't `00`, and the server's parent-based sampler follows that, so a hub call is traced whole
(browser and server) or not at all, as often as before. HTTP calls (a few a page) are always kept. Like any
`traceparent` a client sends, a browser can ask for its calls to be traced; the rate limit and sampling at the share keep
that to what an ordinary tab does.

## Health

| Path | Runs | Used by |
| --- | --- | --- |
| `/health/live` | nothing: answers while the process serves | liveness and startup probes |
| `/health/ready` | the database and the host lifecycle | readiness probe |
| `/health` | everything, including memory (Degraded above `HealthChecks:MaxAllocatedMegabytes`) | people, uptime checks |

Responses are the status word only. Every result is also a metric.

## Behind a proxy

`ForwardedHeaders:KnownIPNetworks` (CIDRs) names the proxies allowed to set the client address and scheme;
`ForwardedHeaders:ForwardedForHeaderName` picks the header (e.g. `X-Real-IP`). With neither set the app trusts no
proxy: sign-in redirects would use `http`, so set them in any deployment behind one.

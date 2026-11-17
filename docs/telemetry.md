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
| `pspsps.hub.call.duration` | Hub method calls, by `method` and `outcome` (`ok`, `refused`, `error`) |

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

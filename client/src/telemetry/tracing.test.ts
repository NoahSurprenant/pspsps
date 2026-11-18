import { HubConnection, NullLogger, TransferFormat } from "@microsoft/signalr";
import { describe, expect, it, vi } from "vitest";
import { AppLog } from "./appLog";
import { ErrorReporter } from "./errors";
import { REDACTED } from "./redact";
import { settle, telemetryOn } from "./testing";
import { HubTracer, TracingHubProtocol, hubOutcome, isOwnCall, tracedFetch } from "./tracing";

const RECORD_SEPARATOR = "\u001e";

/** SignalR's transport, as HubConnection uses it (IConnection, which the package doesn't export). */
interface IConnection {
  readonly features: object;
  baseUrl: string;
  start(format: TransferFormat): Promise<void>;
  send(data: string | ArrayBuffer): Promise<void>;
  stop(error?: unknown): Promise<void>;
  onreceive: ((data: string | ArrayBuffer) => void) | null;
  onclose: ((error?: Error) => void) | null;
}

interface Sent {
  type?: number;
  target?: string;
  invocationId?: string;
  arguments?: unknown[];
  headers?: Record<string, string>;
}

/**
 * A server on the far side of SignalR's real HubConnection: it answers the handshake, records every message the client
 * writes, and completes each call with `answer`.
 */
class FakeServer implements IConnection {
  readonly features = {};
  baseUrl = "http://localhost/hubs/game";
  onreceive: ((data: string | ArrayBuffer) => void) | null = null;
  onclose: ((error?: Error) => void) | null = null;
  readonly messages: Sent[] = [];
  answer: (message: Sent) => object = (message) => ({ type: 3, invocationId: message.invocationId, result: null });

  start(format: TransferFormat): Promise<void> {
    expect(format).toBe(TransferFormat.Text);
    return Promise.resolve();
  }

  send(data: string | ArrayBuffer): Promise<void> {
    for (const frame of String(data).split(RECORD_SEPARATOR).filter(Boolean)) {
      const message = JSON.parse(frame) as Sent;
      if (message.type === undefined) {
        // The handshake: answered with an empty one.
        queueMicrotask(() => this.onreceive?.("{}" + RECORD_SEPARATOR));
        continue;
      }
      this.messages.push(message);
      if (message.type === 1 && message.invocationId !== undefined) {
        const reply = JSON.stringify(this.answer(message)) + RECORD_SEPARATOR;
        queueMicrotask(() => this.onreceive?.(reply));
      }
    }
    return Promise.resolve();
  }

  stop(): Promise<void> {
    this.onclose?.();
    return Promise.resolve();
  }
}

/** SignalR's own HubConnection (its internal factory: the builder would open a real HTTP connection) on `server`. */
function hubOn(server: FakeServer, protocol: TracingHubProtocol): HubConnection {
  const create = (HubConnection as unknown as {
    create(connection: IConnection, logger: typeof NullLogger.instance, protocol: TracingHubProtocol): HubConnection;
  }).create;
  return create(server, NullLogger.instance, protocol);
}

function tracer(sampleRatio: number, random = () => 0.5, on = true) {
  const { telemetry, host } = telemetryOn(on ? sampleRatio : "off");
  const output = { debug: vi.fn(), info: vi.fn(), warn: vi.fn(), error: vi.fn() };
  const hubTracer = new HubTracer(telemetry, new AppLog(telemetry, output), new TracingHubProtocol(), random);
  const sent = () => {
    telemetry.flush();
    return { spans: host.sent.flatMap((b) => b.body.spans), logs: host.sent.flatMap((b) => b.body.logs) };
  };
  return { hubTracer, sent, output };
}

const TRACEPARENT = /^00-([0-9a-f]{32})-([0-9a-f]{16})-(0[01])$/;

describe("hub call tracing, through SignalR's own client", () => {
  it("puts the call's traceparent in its invocation headers, and nowhere else", async () => {
    const server = new FakeServer();
    const { hubTracer, sent } = tracer(1);
    const hub = hubOn(server, hubTracer.protocol);
    await hub.start();

    await hubTracer.invoke("Say", () => hub.invoke("Say", "hello there"));
    // A call made outside the tracer (none should be) carries nothing.
    await hub.invoke("Move", 1, 2);

    const [say, move] = server.messages.filter((m) => m.type === 1);
    const match = TRACEPARENT.exec(say?.headers?.traceparent ?? "");
    expect(match, "the Say call's traceparent").not.toBeNull();
    expect(move?.headers).toBeUndefined();
    const [span] = sent().spans;
    expect(span).toMatchObject({ name: "GameHub/Say", kind: "client", traceId: match?.[1], spanId: match?.[2] });
    expect(span?.attributes).toMatchObject({ "rpc.system": "signalr", "rpc.method": "Say", "pspsps.hub.outcome": "ok" });
    expect(JSON.stringify(span), "never the arguments: chat goes through the hub").not.toContain("hello there");
    await hub.stop();
  });

  it("samples calls at the server's share: an unsampled call says 00 and keeps no span", async () => {
    const server = new FakeServer();
    const { hubTracer, sent } = tracer(0.1, () => 0.5);
    const hub = hubOn(server, hubTracer.protocol);
    await hub.start();

    await hubTracer.invoke("Move", () => hub.invoke("Move", 1, 2));

    expect(server.messages.find((m) => m.type === 1)?.headers?.traceparent).toMatch(/-00$/);
    expect(sent().spans).toEqual([]);
    await hub.stop();
  });

  it("adds no headers with telemetry off", async () => {
    const server = new FakeServer();
    const { hubTracer } = tracer(1, () => 0, false);
    const hub = hubOn(server, hubTracer.protocol);
    await hub.start();

    await hubTracer.invoke("Move", () => hub.invoke("Move", 1, 2));

    const [move] = server.messages.filter((m) => m.type === 1);
    expect(move).toBeDefined();
    expect(move?.headers).toBeUndefined();
    await hub.stop();
  });

  it("logs a refused call as info, without the server's words, in the call's trace", async () => {
    const server = new FakeServer();
    server.answer = (message) => ({ type: 3, invocationId: message.invocationId, error: "HubException: Felix isn't in a room right now." });
    const { hubTracer, sent, output } = tracer(1);
    const hub = hubOn(server, hubTracer.protocol);
    await hub.start();

    await expect(hubTracer.invoke("GoToFriend", () => hub.invoke("GoToFriend", "id"))).rejects.toThrow("Felix");

    const { spans, logs } = sent();
    expect(spans[0]?.attributes["pspsps.hub.outcome"]).toBe("refused");
    expect(spans[0]?.status, "the game working, not an error").toBe("unset");
    expect(logs[0]).toMatchObject({ level: "info", message: "A hub call was refused", traceId: spans[0]?.traceId, spanId: spans[0]?.spanId });
    expect(JSON.stringify(logs)).not.toContain("Felix");
    expect(output.error).not.toHaveBeenCalled();
    await hub.stop();
  });

  it("logs a failed call as an error", async () => {
    const server = new FakeServer();
    server.answer = (message) => ({
      type: 3, invocationId: message.invocationId, error: "An unexpected error occurred invoking 'Reel' on the server.",
    });
    const { hubTracer, sent } = tracer(1);
    const hub = hubOn(server, hubTracer.protocol);
    await hub.start();

    await expect(hubTracer.invoke("Reel", () => hub.invoke("Reel"))).rejects.toThrow();

    const { spans, logs } = sent();
    expect(spans[0]?.status).toBe("error");
    expect(logs[0]).toMatchObject({ level: "error", message: "A hub call failed", attributes: { "rpc.method": "Reel" } });
    expect(logs[0]?.attributes["exception.message"]).toContain("unexpected error occurred invoking 'Reel'");
    await hub.stop();
  });

  it("logs a call cut off with its connection as a warning", async () => {
    const { hubTracer, sent } = tracer(1);
    await expect(hubTracer.invoke("Move", () =>
      Promise.reject(new Error("Invocation canceled due to the underlying connection being closed.")))).rejects.toThrow();
    expect(sent().logs[0]).toMatchObject({ level: "warn", message: "A hub call was cut off with its connection" });
  });
});

describe("hubOutcome", () => {
  it("tells a refusal from a cut-off from a failure", () => {
    expect(hubOutcome(new Error("x. HubException: no"))).toBe("refused");
    expect(hubOutcome(new Error("Cannot send data if the connection is not in the 'Connected' State."))).toBe("cut-off");
    expect(hubOutcome(new Error("boom"))).toBe("error");
  });
});

describe("tracedFetch", () => {
  const base = () => "https://game.example/";

  function fetching(status: number | Error, on = true) {
    const { telemetry, host } = telemetryOn(on ? 0 : "off");
    const reporter = new ErrorReporter(telemetry, { href: base, cancelledByBrowser: () => false });
    const send = vi.fn<typeof fetch>(async () => {
      if (status instanceof Error) {
        throw status;
      }
      return new Response(null, { status });
    });
    const sent = () => {
      telemetry.flush();
      return { spans: host.sent.flatMap((b) => b.body.spans), logs: host.sent.flatMap((b) => b.body.logs) };
    };
    return { fetch: tracedFetch(telemetry, reporter, base, send), send, sent };
  }

  it("sends a traceparent and keeps a client span for each call to the server, sampled whatever the hub share", async () => {
    const { fetch, send, sent } = fetching(200);
    await fetch("/api/me?token=abc");
    const header = new Headers(send.mock.calls[0]?.[1]?.headers).get("traceparent");
    const match = TRACEPARENT.exec(header ?? "");
    expect(match?.[3]).toBe("01");
    const [span] = sent().spans;
    expect(span).toMatchObject({ name: "GET /api/me", kind: "client", traceId: match?.[1], spanId: match?.[2] });
    expect(span?.attributes).toMatchObject({ "url.path": `/api/me?token=${REDACTED}`, "http.response.status_code": 200 });
  });

  it("reports a 5xx and no answer as errors in the call's trace", async () => {
    const failed = fetching(502);
    await failed.fetch("/auth/guest", { method: "POST" });
    const { spans, logs } = failed.sent();
    expect(spans[0]?.status).toBe("error");
    expect(logs[0]).toMatchObject({ message: "POST /auth/guest answered 502", traceId: spans[0]?.traceId });

    const dropped = fetching(new TypeError("Failed to fetch"));
    await expect(dropped.fetch("/api/me")).rejects.toThrow("Failed to fetch");
    expect(dropped.sent().logs[0]?.message).toBe("GET /api/me got no answer: TypeError: Failed to fetch");
  });

  it("leaves a 4xx unreported", async () => {
    const { fetch, sent } = fetching(401);
    await fetch("/api/me");
    expect(sent().logs).toEqual([]);
  });

  it("is plain fetch when off, for other sites, and for its own batches", async () => {
    const off = fetching(200, false);
    await off.fetch("/api/me");
    expect(new Headers(off.send.mock.calls[0]?.[1]?.headers).has("traceparent")).toBe(false);

    const on = fetching(200);
    await on.fetch("https://elsewhere.example/x");
    expect(new Headers(on.send.mock.calls[0]?.[1]?.headers).has("traceparent")).toBe(false);
    expect(isOwnCall("/api/client-telemetry", base())).toBe(false);
    expect(isOwnCall("/api/me", base())).toBe(true);
    await settle();
  });
});

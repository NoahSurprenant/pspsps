import { JsonHubProtocol, MessageType, type HubMessage, type IHubProtocol, type ILogger, type MessageHeaders } from "@microsoft/signalr";
import type { AppLog } from "./appLog";
import { isRefusal, type ErrorReporter } from "./errors";
import { redactUrl } from "./redact";
import { TELEMETRY_URL, type BrowserTelemetry } from "./telemetry";

/** Whether a request goes to the game's own server (same origin), the telemetry's own batches aside. */
export function isOwnCall(url: string, base: string): boolean {
  let parsed: URL;
  try {
    parsed = new URL(url, base);
  } catch {
    return false;
  }
  return parsed.origin === new URL(base).origin && parsed.pathname !== TELEMETRY_URL;
}

/**
 * fetch, with a client span for each call to the game's server and its traceparent sent along, so the server's spans
 * join the trace; a call with no answer (not the browser's doing) or a 5xx is reported as an error, in that trace. Sampled
 * at the server's share of root spans, as hub calls are: a call the browser doesn't keep says 00, and the server drops
 * its spans too, so the server traces these requests as often as it did before. With telemetry off, plain fetch.
 */
export function tracedFetch(
  telemetry: BrowserTelemetry,
  reporter: ErrorReporter,
  base: () => string = () => location.href,
  send: typeof fetch = (input, init) => globalThis.fetch(input, init),
  random: () => number = Math.random,
): (url: string, init?: RequestInit) => Promise<Response> {
  return async (url, init = {}) => {
    if (!telemetry.enabled || !isOwnCall(url, base())) {
      return send(url, init);
    }
    const method = (init.method ?? "GET").toUpperCase();
    const path = redactUrl(url, base());
    const span = telemetry.startSpan(`${method} ${path.split("?")[0]}`, {
      kind: "client",
      sampled: random() < telemetry.sampleRatio,
      attributes: { "http.request.method": method, "url.path": path },
    });
    const headers = new Headers(init.headers);
    headers.set("traceparent", span.traceparent);
    try {
      const response = await send(url, { ...init, headers });
      span.setAttribute("http.response.status_code", response.status);
      if (response.status >= 500) {
        span.setStatus("error");
        reporter.reportCall(method, url, response.status, span);
      }
      return response;
    } catch (error) {
      span.setStatus("error");
      reporter.reportCall(method, url, 0, span, error);
      throw error;
    } finally {
      span.end();
    }
  };
}

/**
 * SignalR's JSON protocol, which can put headers on the next hub call it writes. The hub protocol has headers on every
 * invocation message (the JavaScript client just never sets any); ASP.NET Core SignalR reads a traceparent there and
 * continues the trace in its hub method span (Microsoft.AspNetCore.SignalR.Server). HubConnection.invoke writes its
 * message synchronously, so headers set around the call (withHeaders) go on that call's message and no other.
 */
export class TracingHubProtocol implements IHubProtocol {
  private next: MessageHeaders | undefined;

  constructor(private readonly inner: IHubProtocol = new JsonHubProtocol()) {}

  get name(): string {
    return this.inner.name;
  }

  get version(): number {
    return this.inner.version;
  }

  get transferFormat() {
    return this.inner.transferFormat;
  }

  /** Runs `send` (a hub invoke) with `headers` on the invocation it writes. */
  withHeaders<T>(headers: MessageHeaders, send: () => T): T {
    this.next = headers;
    try {
      return send();
    } finally {
      this.next = undefined;
    }
  }

  parseMessages(input: string | ArrayBuffer, logger: ILogger): HubMessage[] {
    return this.inner.parseMessages(input, logger);
  }

  writeMessage(message: HubMessage): string | ArrayBuffer {
    const headers = this.next;
    if (headers && message.type === MessageType.Invocation) {
      this.next = undefined;
      return this.inner.writeMessage({ ...message, headers: { ...message.headers, ...headers } });
    }
    return this.inner.writeMessage(message);
  }
}

/** What a hub call came to: done, refused by the game (a HubException), cut off with its connection, or failed. */
export type HubOutcome = "ok" | "refused" | "cut-off" | "error";

/** A call that failed because its connection closed or wasn't there: a reconnect or a tab closing, not a bug. */
export function isCutOff(error: unknown): boolean {
  const message = error instanceof Error ? error.message : String(error);
  return message.includes("underlying connection being closed") || message.includes("connection is not in the 'Connected' State");
}

export function hubOutcome(error: unknown): HubOutcome {
  return isRefusal(error) ? "refused" : isCutOff(error) ? "cut-off" : "error";
}

/**
 * A client span for each hub call (GameHub/<method>), its trace context in the call's headers (TracingHubProtocol) for
 * the server's hub span to continue. Sampled at the server's own share of root spans: the traceparent says 00 for a call
 * not sampled, so the server drops its spans too, and a hub call is traced as often as before, whole. A refused call is
 * an info line, one cut off a warning, any other failure an error, each in the call's trace. Never the arguments: chat
 * goes through the hub.
 */
export class HubTracer {
  constructor(
    private readonly telemetry: BrowserTelemetry,
    private readonly log: AppLog,
    readonly protocol = new TracingHubProtocol(),
    private readonly random: () => number = Math.random,
  ) {}

  async invoke<T>(method: string, invoke: () => Promise<T>): Promise<T> {
    if (!this.telemetry.enabled) {
      return invoke();
    }
    const span = this.telemetry.startSpan(`GameHub/${method}`, {
      kind: "client",
      sampled: this.random() < this.telemetry.sampleRatio,
      attributes: { "rpc.system": "signalr", "rpc.service": "GameHub", "rpc.method": method },
    });
    try {
      const result = await this.protocol.withHeaders({ traceparent: span.traceparent }, invoke);
      span.setAttribute("pspsps.hub.outcome", "ok");
      return result;
    } catch (error) {
      const outcome = hubOutcome(error);
      span.setAttribute("pspsps.hub.outcome", outcome);
      const attributes = { "rpc.method": method };
      if (outcome === "refused") {
        // What the server said may name a player: the line says only which call.
        this.log.info("A hub call was refused", undefined, attributes, span);
      } else if (outcome === "cut-off") {
        this.log.warn("A hub call was cut off with its connection", error, attributes, span);
      } else {
        span.setStatus("error");
        this.log.error("A hub call failed", error, attributes, span);
      }
      throw error;
    } finally {
      span.end();
    }
  }
}

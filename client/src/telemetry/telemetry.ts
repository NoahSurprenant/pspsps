import { appVersion } from "./version";

// The browser's telemetry: its spans (a call to the server, a hub call), its log lines and its performance numbers, sent
// in batches to the server (POST /api/client-telemetry, src/Pspsps.Server/Telemetry/Browser), which passes the spans and
// logs on to the collector as the game's and records the numbers as metrics. No OpenTelemetry SDK: the few things it
// needs are here. Nothing is recorded unless the page says telemetry is on (telemetrySettings), from an automated browser
// (tests, Lighthouse), or once the server refuses a batch with a 404 (telemetry is off there).

export const TELEMETRY_URL = "/api/client-telemetry";
/** The Server-Timing entry the server puts on the page when it takes the browser's telemetry; its description is the share of hub calls traced. */
export const TELEMETRY_MARK = "pspsps-telemetry";
export const FLUSH_INTERVAL_MS = 5_000;
/** What one batch carries of each, at most: the server's limits (ClientTelemetryLimits). */
export const MAX_BATCH = { spans: 100, logs: 100, vitals: 20 } as const;
/** What waits to be sent, at most, of each: the oldest go first. */
export const MAX_QUEUE = 500;
/** fetch's keepalive (which outlives the page) takes at most 64 KiB in flight, all its posts together, in bytes. */
export const MAX_KEEPALIVE_BYTES = 60_000;
/** The most a batch's body takes, in bytes: under the server's 256 KiB (ClientTelemetryLimits.BodyBytes), which refuses more whole. */
export const MAX_BATCH_BYTES = 200_000;
/** The server's limits on text and numbers: a batch past them is refused whole. */
export const MAX_SPAN_NAME = 200;
export const MAX_LOG_MESSAGE = 4_000;
export const MAX_VITAL = 600_000;

export type SpanKind = "internal" | "client";
export type SpanStatus = "unset" | "ok" | "error";
export type LogLevel = "debug" | "info" | "warn" | "error";
export type AttributeValue = string | number | boolean;
export type Attributes = Record<string, AttributeValue | undefined>;
/** Web Vitals, then the game's own: frames per second in a room (a median over a while), and long tasks (ms). */
export type VitalName = "LCP" | "INP" | "CLS" | "FCP" | "TTFB" | "FPS" | "LONG_TASK";

/** A span's place in its trace. */
export interface TraceContext {
  traceId: string;
  spanId: string;
}

/** What the server takes (ClientSpanDto): times in epoch milliseconds. */
export interface SpanData extends TraceContext {
  parentSpanId?: string;
  name: string;
  kind: SpanKind;
  start: number;
  end: number;
  status: SpanStatus;
  attributes: Record<string, AttributeValue>;
}

/** ClientLogDto. */
export interface LogData {
  time: number;
  level: LogLevel;
  message: string;
  traceId?: string;
  spanId?: string;
  attributes: Record<string, AttributeValue>;
}

/** ClientVitalDto. */
export interface VitalData {
  name: VitalName;
  value: number;
}

/** ClientTelemetryBatchDto: what the browser collected since its last batch, and the build it runs. */
export interface TelemetryBatch {
  version: string;
  spans: SpanData[];
  logs: LogData[];
  vitals: VitalData[];
}

/** What the page says about telemetry, when it is on. */
export interface TelemetrySettings {
  /** The share of hub calls traced, 0 to 1: the server's own share of root spans. */
  sampleRatio: number;
}

/** `bytes` random bytes as hex: a trace ID (16) or a span ID (8), never all zeros (W3C calls that invalid). */
export function randomId(bytes: number): string {
  const values = new Uint8Array(bytes);
  do {
    crypto.getRandomValues(values);
  } while (values.every((b) => b === 0));
  return [...values].map((b) => b.toString(16).padStart(2, "0")).join("");
}

const TRACEPARENT = /^00-([0-9a-f]{32})-([0-9a-f]{16})-([0-9a-f]{2})$/;

/** A W3C traceparent's trace and span IDs; undefined for anything else. */
export function parseTraceparent(value: string | null | undefined): TraceContext | undefined {
  const match = value ? TRACEPARENT.exec(value) : null;
  return match?.[1] && match[2] ? { traceId: match[1], spanId: match[2] } : undefined;
}

/** `00-<trace>-<span>-<flags>`: flags 01 asks the server to keep its spans, 00 to drop them. */
export function formatTraceparent(context: TraceContext, sampled: boolean): string {
  return `00-${context.traceId}-${context.spanId}-${sampled ? "01" : "00"}`;
}

/**
 * Whether telemetry is on for this page, and how: only when the server marked the page (its Server-Timing, read from the
 * navigation's timing, so no request of its own), and never under automation (navigator.webdriver: the tests) or
 * Lighthouse.
 */
export function telemetrySettings(
  perf: Pick<Performance, "getEntriesByType"> | undefined = globalThis.performance,
  nav: Pick<Navigator, "webdriver" | "userAgent"> | undefined = globalThis.navigator,
): TelemetrySettings | undefined {
  if (!nav || nav.webdriver || nav.userAgent.includes("Chrome-Lighthouse")) {
    return undefined;
  }
  const navigation = perf?.getEntriesByType?.("navigation")[0] as PerformanceNavigationTiming | undefined;
  const mark = navigation?.serverTiming?.find((timing) => timing.name === TELEMETRY_MARK);
  if (!mark) {
    return undefined;
  }
  const ratio = Number(mark.description);
  return { sampleRatio: Number.isFinite(ratio) && ratio >= 0 && ratio <= 1 ? ratio : 0 };
}

/** The time now in epoch milliseconds, to the microsecond where the browser allows. */
export function epochMs(): number {
  return performance.timeOrigin + performance.now();
}

/** Attributes without the ones left undefined. */
function defined(attributes: Attributes): Record<string, AttributeValue> {
  const result: Record<string, AttributeValue> = {};
  for (const [key, value] of Object.entries(attributes)) {
    if (value !== undefined) {
      result[key] = value;
    }
  }
  return result;
}

/** A span in progress; `end` hands it to the batch, if it is sampled. */
export class BrowserSpan implements TraceContext {
  readonly traceId: string;
  readonly spanId = randomId(8);
  readonly parentSpanId?: string;
  readonly start = epochMs();
  private readonly attributes: Attributes;
  private status: SpanStatus = "unset";
  private ended = false;

  constructor(
    private readonly name: string,
    readonly kind: SpanKind,
    parent: TraceContext | undefined,
    attributes: Attributes,
    /** Whether the span is kept, and the server asked to keep its own: unsampled spans still carry a trace. */
    readonly sampled: boolean,
    private readonly done: (span: SpanData) => void,
  ) {
    this.traceId = parent?.traceId ?? randomId(16);
    this.parentSpanId = parent?.spanId;
    this.attributes = { ...attributes };
  }

  /** The traceparent a call made in this span sends, so the server's spans join it (or are dropped with it). */
  get traceparent(): string {
    return formatTraceparent(this, this.sampled);
  }

  setAttribute(key: string, value: AttributeValue | undefined): void {
    this.attributes[key] = value;
  }

  setStatus(status: SpanStatus): void {
    this.status = status;
  }

  end(): void {
    if (this.ended) {
      return;
    }
    this.ended = true;
    if (!this.sampled) {
      return;
    }
    this.done({
      traceId: this.traceId,
      spanId: this.spanId,
      parentSpanId: this.parentSpanId,
      name: this.name.slice(0, MAX_SPAN_NAME),
      kind: this.kind,
      start: this.start,
      end: epochMs(),
      status: this.status,
      attributes: defined(this.attributes),
    });
  }
}

export interface SpanOptions {
  kind?: SpanKind;
  parent?: TraceContext;
  attributes?: Attributes;
  /** Whether it is kept: true unless said (a hub call is sampled at the server's share). */
  sampled?: boolean;
}

/** What the batcher needs of the browser, so tests can hand in their own. */
export interface TelemetryHost {
  fetch: typeof fetch;
  addEventListener(type: "visibilitychange" | "pagehide", listener: () => void): void;
  visibilityState(): DocumentVisibilityState;
  setInterval(handler: () => void, ms: number): unknown;
}

function browserHost(): TelemetryHost {
  return {
    // The real fetch, bound: never the traced one, or every batch would be a span.
    fetch: (input, init) => globalThis.fetch(input, init),
    addEventListener: (type, listener) => (type === "visibilitychange" ? document : window).addEventListener(type, listener),
    visibilityState: () => document.visibilityState,
    setInterval: (handler, ms) => globalThis.setInterval(handler, ms),
  };
}

export class BrowserTelemetry {
  private readonly queue = { spans: [] as SpanData[], logs: [] as LogData[], vitals: [] as VitalData[] };
  private off = false;
  private started = false;
  /** The bytes of keepalive posts not yet answered: their 64 KiB is shared. */
  private keepaliveInFlight = 0;
  /** A random ID for this page load, on every span and line, to read one page's together. No player is in it. */
  readonly sessionId = randomId(16);

  constructor(
    /** Undefined: telemetry is off for the page (telemetrySettings). */
    private readonly settings: TelemetrySettings | undefined,
    private readonly host: TelemetryHost = browserHost(),
    readonly version: string = appVersion,
  ) {}

  /** Whether anything is recorded: on for the page, and not turned off by the server since (a 404). */
  get enabled(): boolean {
    return this.settings !== undefined && !this.off;
  }

  /** The share of hub calls traced. */
  get sampleRatio(): number {
    return this.settings?.sampleRatio ?? 0;
  }

  /** Sends what is queued every few seconds, and all of it as the page hides or goes (keepalive outlives it). Once. */
  start(): void {
    if (!this.enabled || this.started) {
      return;
    }
    this.started = true;
    this.host.setInterval(() => this.flush(), FLUSH_INTERVAL_MS);
    this.host.addEventListener("visibilitychange", () => {
      if (this.host.visibilityState() === "hidden") {
        this.flush(true);
      }
    });
    this.host.addEventListener("pagehide", () => this.flush(true));
  }

  startSpan(name: string, options: SpanOptions = {}): BrowserSpan {
    return new BrowserSpan(name, options.kind ?? "internal", options.parent, options.attributes ?? {},
      options.sampled ?? true, (span) => {
        if (this.enabled) {
          span.attributes["session.id"] = this.sessionId;
          this.push(this.queue.spans, span);
        }
      });
  }

  /** A log line, in `trace` if given. */
  log(level: LogLevel, message: string, attributes: Attributes = {}, trace?: TraceContext): void {
    if (!this.enabled) {
      return;
    }
    this.push(this.queue.logs, {
      time: epochMs(),
      level,
      message: (message || "(empty)").slice(0, MAX_LOG_MESSAGE),
      traceId: trace?.traceId,
      spanId: trace?.spanId,
      attributes: { ...defined(attributes), "session.id": this.sessionId },
    });
  }

  /** A number; one outside the server's range (a tab left open for days) is dropped rather than refuse its batch. */
  vital(name: VitalName, value: number): void {
    if (this.enabled && Number.isFinite(value) && value >= 0 && value <= MAX_VITAL) {
      this.push(this.queue.vitals, { name, value });
    }
  }

  /** Sends what is queued: one batch (up to MAX_BATCH of each), or with `all` (the page going) every batch it takes. */
  flush(all = false): void {
    if (!this.enabled) {
      this.queue.spans.length = this.queue.logs.length = this.queue.vitals.length = 0;
      return;
    }
    do {
      const batch: TelemetryBatch = {
        version: this.version,
        spans: this.queue.spans.splice(0, MAX_BATCH.spans),
        logs: this.queue.logs.splice(0, MAX_BATCH.logs),
        vitals: this.queue.vitals.splice(0, MAX_BATCH.vitals),
      };
      if (!batch.spans.length && !batch.logs.length && !batch.vitals.length) {
        return;
      }
      this.send(this.fitted(batch));
    } while (all);
  }

  /**
   * The batch as JSON, within MAX_BATCH_BYTES: past it (long stacks), the later half of its longest list goes back to the
   * front of the queue, for the next batch, until it fits.
   */
  private fitted(batch: TelemetryBatch): { body: string; bytes: number } {
    for (;;) {
      const body = JSON.stringify(batch);
      const bytes = new TextEncoder().encode(body).length;
      const longest = (["spans", "logs", "vitals"] as const)
        .reduce((most, kind) => (batch[kind].length > batch[most].length ? kind : most));
      if (bytes <= MAX_BATCH_BYTES || batch[longest].length < 2) {
        return { body, bytes };
      }
      const list: unknown[] = batch[longest];
      const queue: unknown[] = this.queue[longest];
      queue.unshift(...list.splice(Math.ceil(list.length / 2)));
    }
  }

  private send({ body, bytes }: { body: string; bytes: number }): void {
    // Keepalive while the posts in flight leave room: past it the browser refuses the post outright, where without
    // keepalive it at least goes while the page lives.
    const keepalive = this.keepaliveInFlight + bytes <= MAX_KEEPALIVE_BYTES;
    if (keepalive) {
      this.keepaliveInFlight += bytes;
    }
    this.host.fetch(TELEMETRY_URL, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body,
      keepalive,
      credentials: "same-origin",
    }).then((response) => {
      // 404: telemetry is off here. Any other refusal (a 400 or 413 for one batch, a 429) drops that batch only.
      if (response.status === 404) {
        this.off = true;
      }
    }).catch(() => {
      // Telemetry is best effort: a batch that doesn't go is dropped.
    }).finally(() => {
      if (keepalive) {
        this.keepaliveInFlight -= bytes;
      }
    });
  }

  private push<T>(list: T[], item: T): void {
    list.push(item);
    if (list.length > MAX_QUEUE) {
      list.splice(0, list.length - MAX_QUEUE);
    }
  }
}

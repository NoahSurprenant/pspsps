import { afterEach, describe, expect, it, vi } from "vitest";
import { buildVersion } from "./buildVersion";
import {
  FLUSH_INTERVAL_MS, MAX_BATCH, MAX_BATCH_BYTES, MAX_KEEPALIVE_BYTES, MAX_QUEUE, TELEMETRY_MARK, TELEMETRY_URL, formatTraceparent,
  parseTraceparent, randomId, telemetrySettings,
} from "./telemetry";
import { settle, telemetryOn } from "./testing";

function navigation(serverTiming: { name: string; description: string }[]): Pick<Performance, "getEntriesByType"> {
  return { getEntriesByType: () => [{ serverTiming } as unknown as PerformanceEntry] };
}

const player = { webdriver: false, userAgent: "Mozilla/5.0 Chrome/140" };

describe("telemetrySettings", () => {
  it("is on with the share of hub calls the server's mark gives", () => {
    expect(telemetrySettings(navigation([{ name: TELEMETRY_MARK, description: "0.25" }]), player)).toEqual({ sampleRatio: 0.25 });
  });

  it("is off without the server's mark", () => {
    expect(telemetrySettings(navigation([{ name: "db", description: "" }]), player)).toBeUndefined();
    expect(telemetrySettings({ getEntriesByType: () => [] }, player)).toBeUndefined();
  });

  it("is off under automation and Lighthouse, mark or not", () => {
    const marked = navigation([{ name: TELEMETRY_MARK, description: "1" }]);
    expect(telemetrySettings(marked, { ...player, webdriver: true })).toBeUndefined();
    expect(telemetrySettings(marked, { ...player, userAgent: "Mozilla/5.0 Chrome-Lighthouse" })).toBeUndefined();
  });

  it("traces no hub call when the share isn't a number from 0 to 1", () => {
    for (const description of ["", "x", "2", "-1"]) {
      expect(telemetrySettings(navigation([{ name: TELEMETRY_MARK, description }]), player)).toEqual({ sampleRatio: 0 });
    }
  });
});

describe("trace IDs", () => {
  it("are random hex of the right length", () => {
    expect(randomId(16)).toMatch(/^[0-9a-f]{32}$/);
    expect(randomId(8)).toMatch(/^[0-9a-f]{16}$/);
    expect(randomId(16)).not.toBe(randomId(16));
  });

  it("make a traceparent that says whether the server keeps its spans", () => {
    const context = { traceId: "a".repeat(32), spanId: "b".repeat(16) };
    expect(formatTraceparent(context, true)).toBe(`00-${"a".repeat(32)}-${"b".repeat(16)}-01`);
    expect(formatTraceparent(context, false)).toBe(`00-${"a".repeat(32)}-${"b".repeat(16)}-00`);
    expect(parseTraceparent(formatTraceparent(context, false))).toEqual(context);
    expect(parseTraceparent("00-nope")).toBeUndefined();
  });
});

describe("BrowserTelemetry", () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it("sends what it has as one batch, with the build's version", () => {
    const { telemetry, host } = telemetryOn();
    telemetry.log("info", "hello", { "rpc.method": "Move" });
    telemetry.vital("LCP", 1200);
    telemetry.startSpan("GET /api/me").end();

    telemetry.flush();

    expect(host.fetch).toHaveBeenCalledOnce();
    expect(host.fetch.mock.calls[0]?.[0]).toBe(TELEMETRY_URL);
    const batch = host.sent[0]?.body;
    expect(batch?.version).toBe("abc1234");
    expect(batch?.logs[0]).toMatchObject({ level: "info", message: "hello", attributes: { "rpc.method": "Move" } });
    expect(batch?.logs[0]?.attributes["session.id"]).toBe(telemetry.sessionId);
    expect(batch?.vitals).toEqual([{ name: "LCP", value: 1200 }]);
    expect(batch?.spans[0]).toMatchObject({ name: "GET /api/me", kind: "internal", status: "unset" });
  });

  it("sends nothing when there is nothing", () => {
    const { telemetry, host } = telemetryOn();
    telemetry.flush();
    expect(host.fetch).not.toHaveBeenCalled();
  });

  it("keeps the newest MAX_QUEUE of each, dropping the oldest", () => {
    const { telemetry, host } = telemetryOn();
    for (let i = 0; i < MAX_QUEUE + 10; i++) {
      telemetry.log("info", `line ${i}`);
    }

    let sent = 0;
    for (let batch = 0; batch < 10; batch++) {
      telemetry.flush();
    }
    for (const { body } of host.sent) {
      sent += body.logs.length;
    }

    expect(sent).toBe(MAX_QUEUE);
    expect(host.sent[0]?.body.logs[0]?.message).toBe("line 10");
  });

  it("sends at most the server's limit of each in one batch", () => {
    const { telemetry, host } = telemetryOn();
    for (let i = 0; i < 150; i++) {
      telemetry.log("info", `line ${i}`);
      telemetry.vital("FPS", 60);
      telemetry.startSpan("span").end();
    }

    telemetry.flush();

    const batch = host.sent[0]?.body;
    expect(batch?.logs).toHaveLength(MAX_BATCH.logs);
    expect(batch?.spans).toHaveLength(MAX_BATCH.spans);
    expect(batch?.vitals).toHaveLength(MAX_BATCH.vitals);
  });

  it("drops numbers out of the server's range rather than have the batch refused", () => {
    const { telemetry, host } = telemetryOn();
    telemetry.vital("LCP", -1);
    telemetry.vital("LCP", Number.NaN);
    telemetry.vital("LCP", 700_000);
    telemetry.vital("CLS", 0.1);
    telemetry.flush();
    expect(host.sent[0]?.body.vitals).toEqual([{ name: "CLS", value: 0.1 }]);
  });

  it("flushes every few seconds, when the page hides and when it goes", () => {
    vi.useFakeTimers();
    const { telemetry, host } = telemetryOn();
    telemetry.start();

    telemetry.log("info", "a");
    vi.advanceTimersByTime(FLUSH_INTERVAL_MS);
    expect(host.fetch).toHaveBeenCalledTimes(1);

    telemetry.log("info", "b");
    host.fire("visibilitychange");
    expect(host.fetch, "still visible: nothing yet").toHaveBeenCalledTimes(1);
    host.visibility = "hidden";
    host.fire("visibilitychange");
    expect(host.fetch).toHaveBeenCalledTimes(2);

    telemetry.log("info", "c");
    host.fire("pagehide");
    expect(host.fetch).toHaveBeenCalledTimes(3);
  });

  it("uses keepalive only for a body that fits its 64 KiB", () => {
    const { telemetry, host } = telemetryOn();
    telemetry.log("info", "small");
    telemetry.flush();
    for (let i = 0; i < 40; i++) {
      telemetry.log("error", `${i} ${"x".repeat(3_000)}`);
    }
    telemetry.flush();

    expect(host.sent[0]?.init.keepalive).toBe(true);
    expect(new TextEncoder().encode(String(host.fetch.mock.calls[1]?.[1]?.body)).length).toBeGreaterThan(MAX_KEEPALIVE_BYTES);
    expect(host.sent[1]?.init.keepalive).toBe(false);
  });

  it("sends everything queued when the page hides or goes, not just one batch", () => {
    const { telemetry, host } = telemetryOn();
    telemetry.start();
    for (let i = 0; i < 250; i++) {
      telemetry.log("info", `line ${i}`);
    }

    host.fire("pagehide");

    expect(host.fetch).toHaveBeenCalledTimes(3);
    expect(host.sent.flatMap(({ body }) => body.logs)).toHaveLength(250);
  });

  it("shares keepalive's 64 KiB among the posts still in flight", async () => {
    const { telemetry, host } = telemetryOn();
    const big = () => {
      for (let i = 0; i < 10; i++) {
        // eslint-disable-next-line sonarjs/pseudo-random -- only makes each message distinct; not security related
        telemetry.log("error", `${Math.random()} ${"x".repeat(3_500)}`);
      }
    };

    big();
    telemetry.flush();
    big();
    telemetry.flush();
    await settle();
    big();
    telemetry.flush();

    expect(host.sent.map(({ init }) => init.keepalive)).toEqual([true, false, true]);
  });

  it("keeps every batch under the server's size limit, sending the rest after", () => {
    const { telemetry, host } = telemetryOn();
    for (let i = 0; i < MAX_BATCH.logs; i++) {
      telemetry.log("error", `${i} ${"x".repeat(3_900)}`, { "exception.stacktrace": "s".repeat(3_900) });
    }

    telemetry.flush(true);

    const sizes = host.fetch.mock.calls.map(([, init]) => new TextEncoder().encode(String(init?.body)).length);
    expect(sizes.length).toBeGreaterThan(1);
    expect(Math.max(...sizes)).toBeLessThanOrEqual(MAX_BATCH_BYTES);
    expect(host.sent.flatMap(({ body }) => body.logs.map((line) => line.message.split(" ")[0]))).toEqual(
      Array.from({ length: MAX_BATCH.logs }, (_, i) => String(i)));
  });

  it("turns off for the page once the server answers 404, and only then", async () => {
    const { telemetry, host } = telemetryOn();
    host.status = 429;
    telemetry.log("info", "a");
    telemetry.flush();
    await settle();
    expect(telemetry.enabled).toBe(true);

    host.status = 404;
    telemetry.log("info", "b");
    telemetry.flush();
    await settle();
    expect(telemetry.enabled).toBe(false);

    telemetry.log("info", "c");
    telemetry.flush();
    expect(host.fetch).toHaveBeenCalledTimes(2);
  });

  it("records and sends nothing when off", () => {
    vi.useFakeTimers();
    const { telemetry, host } = telemetryOn("off");
    telemetry.start();
    telemetry.log("error", "a");
    telemetry.vital("FPS", 60);
    telemetry.startSpan("s").end();
    vi.advanceTimersByTime(FLUSH_INTERVAL_MS * 3);
    host.fire("pagehide");
    expect(host.fetch).not.toHaveBeenCalled();
  });

  it("keeps spans that are sampled, and only those", () => {
    const { telemetry, host } = telemetryOn();
    const kept = telemetry.startSpan("kept");
    const dropped = telemetry.startSpan("dropped", { sampled: false });
    expect(kept.traceparent).toMatch(/-01$/);
    expect(dropped.traceparent).toMatch(/-00$/);
    kept.end();
    dropped.end();
    telemetry.flush();
    expect(host.sent[0]?.body.spans.map((span) => span.name)).toEqual(["kept"]);
  });

  it("ends a span once", () => {
    const { telemetry, host } = telemetryOn();
    const span = telemetry.startSpan("once", { parent: { traceId: "c".repeat(32), spanId: "d".repeat(16) } });
    span.end();
    span.end();
    telemetry.flush();
    expect(host.sent[0]?.body.spans).toHaveLength(1);
    expect(host.sent[0]?.body.spans[0]).toMatchObject({ traceId: "c".repeat(32), parentSpanId: "d".repeat(16) });
  });
});

describe("buildVersion", () => {
  it("is the commit's first seven characters, or dev", () => {
    expect(buildVersion("0123456789abcdef")).toBe("0123456");
    expect(buildVersion(" v1.2 ")).toBe("v1.2");
    expect(buildVersion("")).toBe("dev");
    expect(buildVersion(undefined)).toBe("dev");
    expect(buildVersion("<script>")).toBe("dev");
  });
});

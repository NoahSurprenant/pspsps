import { describe, expect, it, vi } from "vitest";
import { AppLog } from "./appLog";
import {
  FPS_WINDOW_MS, FpsSampler, MAX_BABYLON_LINES, MAX_LONG_TASKS, babylonLine, median, reportLongTasks, watchBabylonLog,
  watchWebGlContext,
} from "./performance";
import { telemetryOn } from "./testing";

/** Frames every `gap` ms from `from` for `ms`. */
function frames(sampler: FpsSampler, from: number, gap: number, ms: number): number {
  let now = from;
  for (; now <= from + ms; now += gap) {
    sampler.frame(now);
  }
  return now;
}

describe("FpsSampler", () => {
  it("reports the median frame rate over each window", () => {
    const report = vi.fn();
    const sampler = new FpsSampler(report, () => true);

    frames(sampler, 0, 1000 / 60, FPS_WINDOW_MS + 20);

    expect(report).toHaveBeenCalledOnce();
    expect(report.mock.calls[0]?.[0]).toBeCloseTo(60, 0);
  });

  it("isn't moved by one long hitch", () => {
    const report = vi.fn();
    const sampler = new FpsSampler(report, () => true);
    let now = frames(sampler, 0, 20, 5_000);
    now += 900;
    frames(sampler, now, 20, FPS_WINDOW_MS);
    expect(report).toHaveBeenCalledOnce();
    expect(report.mock.calls[0]?.[0]).toBe(50);
  });

  it("starts again after a pause (a hidden tab) and outside a room", () => {
    const report = vi.fn();
    let inRoom = true;
    const sampler = new FpsSampler(report, () => inRoom);

    let now = frames(sampler, 0, 16, FPS_WINDOW_MS / 2);
    now += 5_000;
    now = frames(sampler, now, 16, FPS_WINDOW_MS / 2 - 100);
    expect(report, "the pause restarted the window").not.toHaveBeenCalled();

    inRoom = false;
    now = frames(sampler, now, 16, FPS_WINDOW_MS);
    expect(report, "no room, no sample").not.toHaveBeenCalled();

    inRoom = true;
    frames(sampler, now, 16, FPS_WINDOW_MS + 16);
    expect(report).toHaveBeenCalledOnce();
  });
});

describe("median", () => {
  it("is the middle value", () => {
    expect(median([3, 1, 2])).toBe(2);
    expect(median([4, 1, 3, 2])).toBe(2.5);
  });
});

describe("reportLongTasks", () => {
  function observerFor(durations: number[]) {
    let disconnected = false;
    class Observer {
      static supportedEntryTypes = ["longtask"];
      constructor(private readonly callback: (list: { getEntries(): { duration: number }[] }) => void) {}
      observe(): void {
        this.callback({ getEntries: () => durations.map((duration) => ({ duration })) });
      }
      disconnect(): void {
        disconnected = true;
      }
    }
    return { Observer: Observer as unknown as typeof PerformanceObserver, disconnected: () => disconnected };
  }

  it("reports long tasks, at most MAX_LONG_TASKS a page", () => {
    const { telemetry, host } = telemetryOn();
    const { Observer, disconnected } = observerFor(Array.from({ length: MAX_LONG_TASKS + 5 }, () => 72.4));
    reportLongTasks(telemetry, Observer);
    for (let i = 0; i < 5; i++) {
      telemetry.flush();
    }
    const vitals = host.sent.flatMap((batch) => batch.body.vitals);
    expect(vitals).toHaveLength(MAX_LONG_TASKS);
    expect(vitals[0]).toEqual({ name: "LONG_TASK", value: 72 });
    expect(disconnected()).toBe(true);
  });

  it("does nothing where the browser has no long tasks, or telemetry is off", () => {
    const off = telemetryOn("off");
    const { Observer } = observerFor([100]);
    reportLongTasks(off.telemetry, Observer);
    const on = telemetryOn();
    reportLongTasks(on.telemetry, undefined);
    on.telemetry.flush();
    expect(on.host.fetch).not.toHaveBeenCalled();
  });
});

describe("WebGL context", () => {
  it("is a warning when lost and info when back", () => {
    const { telemetry, host } = telemetryOn();
    const output = { debug: vi.fn(), info: vi.fn(), warn: vi.fn(), error: vi.fn() };
    const canvas = new EventTarget();
    watchWebGlContext(canvas as unknown as HTMLCanvasElement, new AppLog(telemetry, output));

    canvas.dispatchEvent(new Event("webglcontextlost"));
    canvas.dispatchEvent(new Event("webglcontextrestored"));
    telemetry.flush();

    expect(host.sent[0]?.body.logs.map((line) => [line.level, line.message])).toEqual([
      ["warn", "The WebGL context was lost"],
      ["info", "The WebGL context was restored"],
    ]);
  });
});

describe("watching again (the scene loaded again after a failed try)", () => {
  it("adds no second listener to the canvas, and keeps one cap on Babylon's lines", () => {
    const { telemetry, host } = telemetryOn();
    const output = { debug: vi.fn(), info: vi.fn(), warn: vi.fn(), error: vi.fn() };
    const log = new AppLog(telemetry, output);
    const canvas = new EventTarget();
    const logger = { OnNewCacheEntry: (entry: string) => void entry };

    for (let attempt = 0; attempt < 3; attempt++) {
      watchWebGlContext(canvas as unknown as HTMLCanvasElement, log);
      watchBabylonLog(logger, log);
    }
    canvas.dispatchEvent(new Event("webglcontextlost"));
    for (let i = 0; i < MAX_BABYLON_LINES * 2; i++) {
      logger.OnNewCacheEntry(`<div style='color:red'>[12:01:02]: error ${i}</div><br>`);
    }
    telemetry.flush();

    expect(output.warn).toHaveBeenCalledOnce();
    expect(host.sent[0]?.body.logs.filter((line) => line.message === "Babylon: error")).toHaveLength(MAX_BABYLON_LINES);
  });
});

describe("Babylon's log", () => {
  it("passes each entry on to a handler set before, rather than replace it", () => {
    const { telemetry } = telemetryOn();
    const before = vi.fn();
    const logger = { OnNewCacheEntry: before as (entry: string) => void };
    watchBabylonLog(logger, new AppLog(telemetry, { debug: vi.fn(), info: vi.fn(), warn: vi.fn(), error: vi.fn() }));

    logger.OnNewCacheEntry("<div style='color:red'>[12:01:02]: boom</div><br>");

    expect(before).toHaveBeenCalledWith("<div style='color:red'>[12:01:02]: boom</div><br>");
  });

  it("reads errors and warnings out of Babylon's entries, and leaves plain logs out", () => {
    expect(babylonLine("<div style='color:red'>[12:01:02]: Unable to compile effect</div><br>"))
      .toEqual({ level: "error", message: "Unable to compile effect" });
    expect(babylonLine("<div style='color:orange'>[12:01:02]: Texture /t.png?sig=x failed</div><br>"))
      .toEqual({ level: "warn", message: "Texture /t.png?sig=REDACTED failed" });
    expect(babylonLine("<div style='color:white'>[12:01:02]: Babylon.js v9</div><br>")).toBeUndefined();
  });

  it("sends Babylon's errors without writing them to the console again, at most MAX_BABYLON_LINES", () => {
    const { telemetry, host } = telemetryOn();
    const output = { debug: vi.fn(), info: vi.fn(), warn: vi.fn(), error: vi.fn() };
    const logger = { OnNewCacheEntry: (entry: string) => void entry };
    watchBabylonLog(logger, new AppLog(telemetry, output));

    for (let i = 0; i < MAX_BABYLON_LINES + 5; i++) {
      logger.OnNewCacheEntry(`<div style='color:red'>[12:01:02]: error ${i}</div><br>`);
    }
    telemetry.flush();

    const logs = host.sent[0]?.body.logs ?? [];
    expect(logs).toHaveLength(MAX_BABYLON_LINES);
    expect(logs[0]).toMatchObject({ level: "error", message: "Babylon: error", attributes: { "error.detail": "error 0", "error.kind": "babylon" } });
    expect(output.error).not.toHaveBeenCalled();
  });
});

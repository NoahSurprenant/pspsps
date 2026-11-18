import type { AppLog } from "./appLog";
import { redactUrlsIn } from "./redact";
import type { BrowserTelemetry } from "./telemetry";

/** Page-load Web Vitals, from web-vitals, loaded (its own small chunk) only when telemetry is on. */
export function reportWebVitals(telemetry: BrowserTelemetry): void {
  if (!telemetry.enabled) {
    return;
  }
  void import("web-vitals").then(({ onCLS, onFCP, onINP, onLCP, onTTFB }) => {
    onCLS((metric) => telemetry.vital("CLS", metric.value));
    onFCP((metric) => telemetry.vital("FCP", metric.value));
    onINP((metric) => telemetry.vital("INP", metric.value));
    onLCP((metric) => telemetry.vital("LCP", metric.value));
    onTTFB((metric) => telemetry.vital("TTFB", metric.value));
  }).catch(() => {
    // Only the vitals: a chunk that didn't load (a deploy replaced it) costs nothing else.
  });
}

/** How long each frame-rate sample runs, in ms. */
export const FPS_WINDOW_MS = 10_000;
/** A gap between frames longer than this (ms) is a pause (a hidden tab, a debugger), not a slow frame: the sample restarts. */
export const MAX_FRAME_GAP_MS = 1_000;
/** A sample needs at least this many frames to say anything. */
export const MIN_FRAMES = 10;

/** The middle value of `values` (sorted in place). */
export function median(values: number[]): number {
  values.sort((a, b) => a - b);
  const middle = values.length >> 1;
  return values.length % 2 ? values[middle] ?? 0 : ((values[middle - 1] ?? 0) + (values[middle] ?? 0)) / 2;
}

/**
 * The frame rate in a room: each frame's time (`frame`, from the engine's end of frame), and every FPS_WINDOW_MS the
 * median frame rate over the window (1000 / the median gap between frames) goes to `report`. The median, not the mean,
 * so one hitch doesn't hide a smooth room nor one burst a slow one. Frames while no room shows (`inRoom` false) or after
 * a pause start the sample again.
 */
export class FpsSampler {
  private gaps: number[] = [];
  private last: number | undefined;
  private started: number | undefined;

  constructor(private readonly report: (fps: number) => void, private readonly inRoom: () => boolean) {}

  frame(now: number): void {
    if (!this.inRoom()) {
      this.reset();
      return;
    }
    if (this.last !== undefined) {
      const gap = now - this.last;
      if (gap > MAX_FRAME_GAP_MS || gap <= 0) {
        this.reset();
      } else {
        this.gaps.push(gap);
      }
    }
    this.last = now;
    this.started ??= now;
    if (now - this.started >= FPS_WINDOW_MS) {
      if (this.gaps.length >= MIN_FRAMES) {
        this.report(Math.round((1000 / median(this.gaps)) * 10) / 10);
      }
      this.gaps = [];
      this.started = now;
    }
  }

  private reset(): void {
    this.gaps = [];
    this.last = undefined;
    this.started = undefined;
  }
}

/** The most long tasks a page reports: one janky device mustn't fill every batch. */
export const MAX_LONG_TASKS = 30;

/** Long tasks (the main thread busy 50 ms or more), where the browser reports them, as LONG_TASK numbers. */
export function reportLongTasks(
  telemetry: BrowserTelemetry,
  Observer: typeof PerformanceObserver | undefined = globalThis.PerformanceObserver,
): void {
  if (!telemetry.enabled || !Observer?.supportedEntryTypes?.includes("longtask")) {
    return;
  }
  let reported = 0;
  const observer = new Observer((list) => {
    for (const entry of list.getEntries()) {
      if (reported >= MAX_LONG_TASKS) {
        observer.disconnect();
        return;
      }
      reported++;
      telemetry.vital("LONG_TASK", Math.round(entry.duration));
    }
  });
  observer.observe({ type: "longtask", buffered: true });
}

// What is watched already: the scene is made again after a failed load (lazy.ts), on the same canvas and logger.
const watched = new WeakSet<object>();

/** WebGL losing its context (the GPU reset, too many tabs drawing) and getting it back, on the game's canvas. Once a canvas. */
export function watchWebGlContext(canvas: Pick<HTMLCanvasElement, "addEventListener">, log: AppLog): void {
  if (watched.has(canvas)) {
    return;
  }
  watched.add(canvas);
  canvas.addEventListener("webglcontextlost", () => log.warn("The WebGL context was lost"));
  canvas.addEventListener("webglcontextrestored", () => log.info("The WebGL context was restored"));
}

/** The most Babylon lines a page sends; Babylon repeats itself. */
export const MAX_BABYLON_LINES = 20;

/**
 * A line of Babylon's log (Logger.OnNewCacheEntry hands each as `<div style='color:red'>[hh:mm:ss]: message</div><br>`):
 * its level (red is an error, orange a warning; plain logs are left out) and its text.
 */
export function babylonLine(entry: string): { level: "error" | "warn"; message: string } | undefined {
  const level = entry.includes("color:red") ? "error" : entry.includes("color:orange") ? "warn" : undefined;
  if (!level) {
    return undefined;
  }
  const text = entry.replace(/<[^>]*>/g, "").replace(/^\[\d\d:\d\d:\d\d\]:\s*/, "").trim();
  return { level, message: redactUrlsIn(text) };
}

/** Babylon's own errors and warnings (a shader that didn't compile, a texture that didn't load), as the game's log lines. Once. */
export function watchBabylonLog(logger: { OnNewCacheEntry: (entry: string) => void }, log: AppLog): void {
  if (watched.has(logger)) {
    return;
  }
  watched.add(logger);
  let sent = 0;
  logger.OnNewCacheEntry = (entry) => {
    const line = babylonLine(entry);
    if (!line || sent >= MAX_BABYLON_LINES) {
      return;
    }
    sent++;
    // Babylon has written it to the console already: only the telemetry's copy goes, with the text as its detail.
    log.send(line.level, `Babylon: ${line.level}`, line.message, { "error.kind": "babylon" });
  };
}

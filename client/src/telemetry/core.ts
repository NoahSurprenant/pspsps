import { AppLog } from "./appLog";
import { ErrorReporter } from "./errors";
import { reportLongTasks, reportWebVitals } from "./performance";
import { BrowserTelemetry, telemetrySettings } from "./telemetry";

// The page's one telemetry (docs/telemetry.md, "From the browser"): on only when the server marked the page, never
// under automation. Everything here is a no-op when it is off. Without the hub and HTTP tracing (index.ts), which pull
// in SignalR's protocol: the admin page uses only this.

const inBrowser = typeof window !== "undefined" && typeof document !== "undefined";

export const telemetry = new BrowserTelemetry(inBrowser ? telemetrySettings() : undefined);
export const errors = new ErrorReporter(telemetry);
/** The game's log: the console, and the telemetry at info and above. */
export const appLog = new AppLog(telemetry);

export interface StartOptions {
  /** Web Vitals and long tasks: the game's page load. Off for another page, whose numbers would mix with the game's. */
  performance?: boolean;
}

/** Starts sending, when on: the batches and uncaught errors, and (unless told not to) Web Vitals and long tasks. */
export function startTelemetry({ performance = true }: StartOptions = {}): void {
  if (!telemetry.enabled) {
    return;
  }
  telemetry.start();
  errors.install(window);
  if (performance) {
    reportWebVitals(telemetry);
    reportLongTasks(telemetry);
  }
}

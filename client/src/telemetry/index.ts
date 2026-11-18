import { AppLog } from "./appLog";
import { ErrorReporter } from "./errors";
import { reportLongTasks, reportWebVitals } from "./performance";
import { BrowserTelemetry, telemetrySettings } from "./telemetry";
import { HubTracer, tracedFetch } from "./tracing";

// The page's one telemetry (docs/telemetry.md, "From the browser"): on only when the server marked the page, never
// under automation. Everything here is a no-op when it is off.

const inBrowser = typeof window !== "undefined" && typeof document !== "undefined";

export const telemetry = new BrowserTelemetry(inBrowser ? telemetrySettings() : undefined);
export const errors = new ErrorReporter(telemetry);
/** The game's log: the console, and the telemetry at info and above. */
export const appLog = new AppLog(telemetry);
/** Traces hub calls; its protocol goes on the hub connection. */
export const hubTracer = new HubTracer(telemetry, appLog);
/** fetch for the game's own server: traced, and its failures reported. */
export const serverFetch = tracedFetch(telemetry, errors);

/** Starts sending, when on: the batches, uncaught errors, Web Vitals and long tasks. First thing in main.ts. */
export function startTelemetry(): void {
  if (!telemetry.enabled) {
    return;
  }
  telemetry.start();
  errors.install(window);
  reportWebVitals(telemetry);
  reportLongTasks(telemetry);
}

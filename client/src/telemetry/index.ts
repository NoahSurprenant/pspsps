import { appLog, errors, telemetry } from "./core";
import { HubTracer, tracedFetch } from "./tracing";

export { appLog, errors, startTelemetry, telemetry } from "./core";

/** Traces hub calls; its protocol goes on the hub connection. */
export const hubTracer = new HubTracer(telemetry, appLog);
/** fetch for the game's own server: traced, and its failures reported. */
export const serverFetch = tracedFetch(telemetry, errors);

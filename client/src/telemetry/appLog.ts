import { describeError } from "./errors";
import type { Attributes, BrowserTelemetry, LogLevel, TraceContext } from "./telemetry";

/** The most lines of each level a page sends: a loop that logs sends no more, and info can't crowd out an error. */
export const MAX_LOGS = 50;

/** What a line says about its cause, as the error reports say it: an error's message and stack, or anything else as text. */
export function causeAttributes(cause: unknown): Attributes {
  if (cause === undefined) {
    return {};
  }
  const { message, stack } = describeError(cause);
  return cause instanceof Error
    ? { "exception.message": message, "exception.stacktrace": stack }
    : { "error.detail": message };
}

/** What the log needs of the console, so tests can hand in their own. */
export type LogConsole = Pick<Console, LogLevel>;

/**
 * The game's log: each line goes to the console, as it always has, and, at info and above, to the browser's telemetry
 * (when it is on) with its cause's details. The telemetry gets each line once a page, and at most MAX_LOGS of each level.
 * Never put chat, names or IDs in a line or its attributes: they go to the collector as they are.
 */
export class AppLog {
  private readonly sent = new Set<string>();
  private readonly sentOf: Record<LogLevel, number> = { debug: 0, info: 0, warn: 0, error: 0 };

  constructor(private readonly telemetry: BrowserTelemetry, private readonly output: LogConsole = console) {}

  /** Something failed (the scene didn't load, a hub call broke). */
  error(message: string, cause?: unknown, attributes?: Attributes, trace?: TraceContext): void {
    this.write("error", message, cause, attributes, trace);
  }

  /** Something unexpected the game got past (a dropped connection, a lost WebGL context). */
  warn(message: string, cause?: unknown, attributes?: Attributes, trace?: TraceContext): void {
    this.write("warn", message, cause, attributes, trace);
  }

  /** Something worth knowing happened (connected, a call the game refused). */
  info(message: string, cause?: unknown, attributes?: Attributes, trace?: TraceContext): void {
    this.write("info", message, cause, attributes, trace);
  }

  /** Detail for following a flow while debugging: the console only. */
  debug(message: string, cause?: unknown, attributes?: Attributes): void {
    this.write("debug", message, cause, attributes);
  }

  /** A line for the telemetry only, for what is on the console already (Babylon writes its own). */
  send(level: Exclude<LogLevel, "debug">, message: string, cause?: unknown, attributes: Attributes = {}, trace?: TraceContext): void {
    if (!this.telemetry.enabled || this.sentOf[level] >= MAX_LOGS) {
      return;
    }
    const lineAttributes = { ...causeAttributes(cause), ...attributes };
    // Not the stack: the same failure from another call site is the same line.
    const key = JSON.stringify([level, message, { ...lineAttributes, "exception.stacktrace": undefined }]);
    if (this.sent.has(key)) {
      return;
    }
    this.sent.add(key);
    this.sentOf[level]++;
    this.telemetry.log(level, message, lineAttributes, trace);
  }

  private write(level: LogLevel, message: string, cause: unknown, attributes: Attributes = {}, trace?: TraceContext): void {
    if (cause === undefined) {
      this.output[level](message);
    } else {
      this.output[level](message, cause);
    }
    if (level !== "debug") {
      this.send(level, message, cause, attributes, trace);
    }
  }
}

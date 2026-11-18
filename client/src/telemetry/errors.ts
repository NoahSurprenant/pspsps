import { redactUrl, redactUrlsIn } from "./redact";
import type { BrowserTelemetry, TraceContext } from "./telemetry";

/**
 * The browser's error reports: uncaught errors and unhandled rejections, and failed calls to the server, as error log
 * lines of the browser's telemetry, with their page (redacted) and, for a call, its trace. A page load reports at most
 * MAX_REPORTS, and each error once; nothing at all when telemetry is off.
 */
export const MAX_REPORTS = 10;
export const MAX_MESSAGE = 1000;
export const MAX_STACK = 4000;
export const MAX_PATH = 300;

/** What a report says for an error thrown without a message (`throw ""`). */
export const NO_MESSAGE = "(no message)";

/** What stands for a refused hub call's message from the server: it may name a player ("Felix isn't in a room"). */
export const HUB_MESSAGE_LEFT_OUT = "(the server's message)";

const HUB_EXCEPTION = "HubException: ";

/** `text` without what the server said in a refused hub call (SignalR puts it after "HubException: "). */
export function withoutHubMessage(text: string): string {
  const at = text.indexOf(HUB_EXCEPTION);
  return at < 0 ? text : text.slice(0, at + HUB_EXCEPTION.length) + HUB_MESSAGE_LEFT_OUT;
}

/** Whether a hub call failed because the game said no (a HubException): the player saw why. */
export function isRefusal(error: unknown): boolean {
  return error instanceof Error && error.message.includes(HUB_EXCEPTION);
}

/**
 * An error's message and stack, whatever was thrown: sensitive query values in them redacted, a refused hub call's
 * message left out, and both cut to size.
 */
export function describeError(error: unknown): { message: string; stack?: string } {
  if (error instanceof Error) {
    const stack = error.stack === undefined ? undefined : clean(error.stack).slice(0, MAX_STACK);
    return { message: clean(`${error.name}: ${error.message.trim() || NO_MESSAGE}`).slice(0, MAX_MESSAGE), stack };
  }
  let message: string;
  try {
    message = typeof error === "string" ? error : JSON.stringify(error) ?? String(error);
  } catch {
    message = String(error);
  }
  // The server takes no blank message.
  return { message: clean(message.trim() || NO_MESSAGE).slice(0, MAX_MESSAGE) };
}

function clean(text: string): string {
  return redactUrlsIn(withoutHubMessage(text));
}

/** A URL as a report carries it: its path and query, sensitive query values redacted, cut to size. */
export function reportedUrl(url: string | null | undefined, base: string): string {
  return redactUrl(url, base).slice(0, MAX_PATH);
}

/** Whether a failed call is worth reporting: no answer at all, or a server error. A 4xx is the game working. */
export function isReportedStatus(status: number): boolean {
  return status === 0 || status >= 500;
}

/** What the reporter needs of the page. */
export interface ReporterHost {
  href(): string;
  /** Whether a call with no answer is the browser's doing: the page hidden or going, or offline. */
  cancelledByBrowser(): boolean;
}

function browserHost(): ReporterHost {
  let leaving = false;
  if (typeof window !== "undefined") {
    window.addEventListener("pagehide", () => (leaving = true));
    // Back from the back/forward cache: the page is live again.
    window.addEventListener("pageshow", () => (leaving = false));
  }
  return {
    href: () => location.href,
    cancelledByBrowser: () => leaving || document.visibilityState === "hidden" || !navigator.onLine,
  };
}

export class ErrorReporter {
  private sent = 0;
  private readonly seen = new Set<string>();

  constructor(private readonly telemetry: BrowserTelemetry, private readonly host: ReporterHost = browserHost()) {}

  /** Reports uncaught errors and unhandled rejections from now on. */
  install(target: Pick<Window, "addEventListener">): void {
    if (!this.telemetry.enabled) {
      return;
    }
    target.addEventListener("error", (event) => this.reportError(event.error ?? event.message, "uncaught"));
    target.addEventListener("unhandledrejection", (event) => this.reportError(event.reason, "unhandledrejection"));
  }

  /** An error nothing caught (or one the game caught but can't go on from), with its kind. */
  reportError(error: unknown, kind: string, trace?: TraceContext): void {
    if (!this.canSend()) {
      return;
    }
    const { message, stack } = describeError(error);
    this.send(`${kind}|${message}`, message, { "error.kind": kind, "exception.stacktrace": stack }, trace);
  }

  /** A call to the server that got no answer (not the browser's doing) or a 5xx, in its own trace. */
  reportCall(method: string, url: string, status: number, trace?: TraceContext, error?: unknown): void {
    if (!this.canSend() || !isReportedStatus(status) || (status === 0 && this.host.cancelledByBrowser())) {
      return;
    }
    const path = reportedUrl(url, this.host.href());
    const message = status === 0
      ? `${method} ${path} got no answer${error === undefined ? "" : `: ${describeError(error).message}`}`
      : `${method} ${path} answered ${status}`;
    this.send(`http|${method}|${path}|${status}`, message.slice(0, MAX_MESSAGE), {
      "error.kind": "http",
      "http.request.method": method,
      "url.path": path,
      "http.response.status_code": status,
    }, trace);
  }

  private canSend(): boolean {
    return this.telemetry.enabled && this.sent < MAX_REPORTS;
  }

  private send(key: string, message: string, attributes: Record<string, string | number | undefined>, trace?: TraceContext): void {
    if (this.seen.has(key)) {
      return;
    }
    this.seen.add(key);
    this.sent++;
    this.telemetry.log("error", message, { ...attributes, page: reportedUrl(this.host.href(), this.host.href()) }, trace);
    // An error as the page goes away: sent now, while it still can be.
    if (this.host.cancelledByBrowser()) {
      this.telemetry.flush();
    }
  }
}

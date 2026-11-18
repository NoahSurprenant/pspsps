import { vi } from "vitest";
import { BrowserTelemetry, type TelemetryBatch, type TelemetryHost, type TelemetrySettings } from "./telemetry";

/** A browser for the batcher: its fetch records what is sent, and the test fires the page's events. */
export class FakeHost implements TelemetryHost {
  readonly sent: { body: TelemetryBatch; init: RequestInit }[] = [];
  status = 204;
  visibility: DocumentVisibilityState = "visible";
  private readonly listeners = new Map<string, (() => void)[]>();

  fetch = vi.fn(async (_input: RequestInfo | URL, init?: RequestInit) => {
    this.sent.push({ body: JSON.parse(String(init?.body)) as TelemetryBatch, init: init ?? {} });
    return new Response(null, { status: this.status });
  });

  addEventListener(type: string, listener: () => void): void {
    this.listeners.set(type, [...(this.listeners.get(type) ?? []), listener]);
  }

  fire(type: string): void {
    for (const listener of this.listeners.get(type) ?? []) {
      listener();
    }
  }

  visibilityState(): DocumentVisibilityState {
    return this.visibility;
  }

  setInterval(handler: () => void, ms: number): unknown {
    return globalThis.setInterval(handler, ms);
  }
}

/** Telemetry that is on (with the share of hub calls traced), or "off", sending through a FakeHost. */
export function telemetryOn(sampleRatio: number | "off" = 1): { telemetry: BrowserTelemetry; host: FakeHost } {
  const host = new FakeHost();
  const settings: TelemetrySettings | undefined = sampleRatio === "off" ? undefined : { sampleRatio };
  return { telemetry: new BrowserTelemetry(settings, host, "abc1234"), host };
}

/** Lets the promises queued so far settle. */
export function settle(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

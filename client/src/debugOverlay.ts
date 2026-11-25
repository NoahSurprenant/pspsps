import { debugLines, DebugStats, type DebugView } from "./debugStats";

export const DEBUG_KEY = "pspsps.debug";

/** Whether the overlay starts shown: asked for in the address (?debug) or left on last time. */
export function debugWanted(search: string, storage: Pick<Storage, "getItem">): boolean {
  const asked = new URLSearchParams(search).get("debug");
  return asked !== null ? asked !== "0" && asked !== "off" : storage.getItem(DEBUG_KEY) === "1";
}

/** A small box in the corner with the room, frame rate and connection health; F3 or ?debug shows it. */
export interface DebugBox { hidden: boolean | string; textContent: string | null }
export interface KeyTarget { addEventListener(type: "keydown", listener: (event: { code: string; altKey: boolean; ctrlKey: boolean; metaKey: boolean; preventDefault(): void }) => void): void }

export class DebugOverlay {
  readonly stats = new DebugStats();
  private timer: ReturnType<typeof setInterval> | undefined;
  private pinging = false;
  private lastPingSlot = -1;

  constructor(
    private readonly view: () => DebugView | undefined,
    private readonly ping: () => Promise<void>,
    private readonly storage: Pick<Storage, "getItem" | "setItem">,
    search: string,
    private readonly box: DebugBox,
    keys: KeyTarget,
  ) {
    this.box.hidden = true;
    keys.addEventListener("keydown", (event) => {
      if (event.code === "F3" && !event.altKey && !event.ctrlKey && !event.metaKey) {
        event.preventDefault();
        this.toggle();
      }
    });
    if (debugWanted(search, storage)) {
      this.show();
    }
  }

  get shown(): boolean {
    return !this.box.hidden;
  }

  toggle(): void {
    if (this.shown) {
      this.hide();
    } else {
      this.show();
    }
  }

  show(): void {
    this.storage.setItem(DEBUG_KEY, "1");
    this.box.hidden = false;
    this.timer ??= globalThis.setInterval(() => this.tick(), 500);
    this.tick();
  }

  hide(): void {
    this.storage.setItem(DEBUG_KEY, "0");
    this.box.hidden = true;
    globalThis.clearInterval(this.timer);
    this.timer = undefined;
  }

  private tick(): void {
    const now = performance.now();
    this.box.textContent = debugLines(this.stats, this.view(), now).join("\n");
    if (!this.pinging && this.shown && Math.floor(now / 2000) !== this.lastPingSlot) {
      this.lastPingSlot = Math.floor(now / 2000);
      this.pinging = true;
      const sent = performance.now();
      this.ping().then(
        () => this.stats.pinged(performance.now() - sent, performance.now()),
        () => this.stats.note("no reply to the ping", performance.now()),
      ).finally(() => { this.pinging = false; });
    }
  }
}

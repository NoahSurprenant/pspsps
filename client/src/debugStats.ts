/** What the debug overlay reports: the frame rate, the slowest recent frame, the server's round trip and the connection's blips. Pure: the page feeds it. */

const FRAME_WINDOW_MS = 1000;
const WORST_WINDOW_MS = 5000;
const MAX_EVENTS = 6;
const PINGS_KEPT = 30;
/** A round trip slower than this is a blip worth a line. */
export const SLOW_PING_MS = 400;

export interface NetEvent { at: number; text: string }

export class DebugStats {
  private frames: number[] = [];
  private gaps: { at: number; gap: number }[] = [];
  private pings: number[] = [];
  readonly events: NetEvent[] = [];

  frame(now: number): void {
    const last = this.frames.at(-1);
    this.frames.push(now);
    if (last !== undefined) {
      this.gaps.push({ at: now, gap: now - last });
    }
    this.frames = this.frames.filter((at) => now - at <= FRAME_WINDOW_MS);
    this.gaps = this.gaps.filter((g) => now - g.at <= WORST_WINDOW_MS);
  }

  /** Frames drawn in the last second. */
  fps(now: number): number {
    return this.frames.filter((at) => now - at <= FRAME_WINDOW_MS).length;
  }

  /** The longest wait between two frames in the last few seconds, in ms; 0 with none. */
  worstFrame(now: number): number {
    return Math.round(Math.max(0, ...this.gaps.filter((g) => now - g.at <= WORST_WINDOW_MS).map((g) => g.gap)));
  }

  pinged(ms: number, now: number): void {
    this.pings.push(ms);
    this.pings = this.pings.slice(-PINGS_KEPT);
    if (ms >= SLOW_PING_MS) {
      this.note(`slow reply, ${Math.round(ms)} ms`, now);
    }
  }

  note(text: string, now: number): void {
    this.events.push({ at: now, text });
    this.events.splice(0, Math.max(0, this.events.length - MAX_EVENTS));
  }

  lastPing(): number | undefined {
    return this.pings.at(-1);
  }

  worstPing(): number | undefined {
    return this.pings.length ? Math.max(...this.pings) : undefined;
  }
}

export interface DebugView {
  roomName: string;
  roomId: string;
  spot?: { x: number; z: number };
  cats: number;
  connection: string;
}

/** The overlay's text, one line each. */
export function debugLines(stats: DebugStats, view: DebugView | undefined, now: number): string[] {
  const ping = stats.lastPing();
  const worst = stats.worstPing();
  const lines = [
    view ? `Room: ${view.roomName} (${view.roomId})` : "Room: none",
    ...(view?.spot ? [`Cat at: ${view.spot.x.toFixed(1)}, ${view.spot.z.toFixed(1)}  ·  ${view.cats} cats here`] : []),
    `Frames: ${stats.fps(now)} fps  ·  worst gap ${stats.worstFrame(now)} ms`,
    `Server: ${view?.connection ?? "no connection"}  ·  ping ${ping === undefined ? "-" : `${Math.round(ping)} ms`}${worst === undefined ? "" : ` (worst ${Math.round(worst)})`}`,
  ];
  for (const event of stats.events) {
    lines.push(`  ${Math.max(0, Math.round((now - event.at) / 1000))}s ago: ${event.text}`);
  }
  return lines;
}

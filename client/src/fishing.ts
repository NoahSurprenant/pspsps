import type { Player, PondView, ReelView } from "./types";

/**
 * Where the player's line is: nothing out, being cast, in the water, a fish on it right now, or being reeled in. While
 * casting or reeling, the buttons wait for the server: a second click does nothing.
 */
export type Fishing = "idle" | "casting" | "waiting" | "bite" | "reeling";

/**
 * The player's line as the client sees it, and its only way to change: each step that sends something to the server
 * says whether to send it, and a cast's answer counts only for the cast that's still going.
 */
export class FishingState {
  private current: Fishing = "idle";
  private casts = 0;

  constructor(private readonly changed: (state: Fishing) => void = () => undefined) {}

  get state(): Fishing {
    return this.current;
  }

  /** Starts a cast from idle: the cast's number, to finish it with, or undefined (already casting or fishing). */
  startCast(): number | undefined {
    if (this.current !== "idle") {
      return undefined;
    }
    this.casts += 1;
    this.set("casting");
    return this.casts;
  }

  /** The cast is in the water: wait for a bite, unless the line was dropped meanwhile (a walk off, another room). */
  castDone(cast: number): void {
    if (this.current === "casting" && cast === this.casts) {
      this.set("waiting");
    }
  }

  /** The cast was refused. */
  castFailed(cast: number): void {
    if (this.current === "casting" && cast === this.casts) {
      this.set("idle");
    }
  }

  /** The server says a fish bit: only a line in the water can have one. */
  bite(): void {
    if (this.current === "waiting") {
      this.set("bite");
    }
  }

  /** Starts reeling a line that's in the water: whether to send Reel (once, however many clicks and keys). */
  startReel(): boolean {
    if (this.current !== "waiting" && this.current !== "bite") {
      return false;
    }
    this.set("reeling");
    return true;
  }

  /** The reel is answered, however it went: the line is out of the water. */
  reeled(): void {
    if (this.current === "reeling") {
      this.set("idle");
    }
  }

  /** The line is out of the water without a reel: the cat walked off, or another room is showing. */
  drop(): void {
    this.set("idle");
  }

  private set(state: Fishing): void {
    this.current = state;
    this.changed(state);
  }
}

/** The player after a reel: a catch's coins are theirs from now on (a world picked again shows them). */
export function paid(player: Player, reel: ReelView): Player {
  return reel.isCatch ? { ...player, coins: reel.totalCoins } : player;
}

/** How far out from the water a cat stands to fish (the server allows up to 1.5). */
const standOff = 0.8;

/**
 * The spot at the pond's edge nearest to <cat>, where a cat stands to fish. A cat standing on the pond's centre goes to
 * the edge in front of it (towards the camera).
 */
export function pondEdge(pond: PondView, cat: { x: number; z: number }): { x: number; z: number } {
  const dx = cat.x - pond.x;
  const dz = cat.z - pond.z;
  const length = Math.hypot(dx, dz);
  const distance = pond.radius + standOff;
  if (length < 1e-6) {
    return { x: pond.x, z: pond.z + distance };
  }
  return { x: pond.x + (dx / length) * distance, z: pond.z + (dz / length) * distance };
}

/** What the player is told after reeling in. */
export function reelMessage(reel: ReelView): string {
  switch (reel.outcome) {
    case "caught":
      return `You caught ${article(reel.fishName ?? "fish")}! +${reel.coins} ${reel.coins === 1 ? "coin" : "coins"}`;
    case "too-early":
      return "Too soon! It swam off.";
    case "too-late":
      return "Too slow! It got away.";
  }
}

/** The bubble over a cat that caught something, seen by everyone in the room. */
export function catchBubble(fishName: string): string {
  return `🐟 ${fishName}!`;
}

function article(name: string): string {
  return /^[aeiou]/i.test(name) ? `an ${name.toLowerCase()}` : `a ${name.toLowerCase()}`;
}

import type { PondView, ReelView } from "./types";

/** Where the player's line is: nothing out, in the water, or a fish on it right now. */
export type Fishing = "idle" | "waiting" | "bite";

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

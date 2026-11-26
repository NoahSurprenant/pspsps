import type { TerraceView } from "./types";

type Spot = { x: number; z: number };

/** How far the steps round a terrace reach out from its top, in metres. */
export const stepsReach = 1;

const stepRise = 0.2;

export interface Slab {
  x: number;
  z: number;
  width: number;
  depth: number;
  /** How high the slab's top is above the ground. */
  top: number;
  /** What paints it instead of stone: a carpet. */
  colour?: string;
  /** An oval inscribed in the width and depth, not a rectangle. */
  round?: boolean;
}

/** A terrace as stacked slabs, the widest and lowest first: the steps up to its top. */
export function slabs(terrace: TerraceView): Slab[] {
  const count = Math.max(1, Math.round(terrace.height / stepRise));
  return Array.from({ length: count }, (_, index) => {
    const outward = (stepsReach * (count - 1 - index)) / count;
    return { x: terrace.x, z: terrace.z, width: terrace.width + 2 * outward, depth: terrace.depth + 2 * outward, top: (terrace.height * (index + 1)) / count, colour: terrace.colour ?? undefined, round: terrace.round ?? undefined };
  });
}

/** How high the ground is at a spot: the top of the highest slab of any terrace under it, else 0. */
export function groundHeight(terraces: readonly TerraceView[], spot: Spot): number {
  let height = 0;
  for (const terrace of terraces) {
    for (const slab of slabs(terrace)) {
      if (slab.top > height && covers(slab, spot)) {
        height = slab.top;
      }
    }
  }
  return height;
}

function covers(slab: Slab, spot: Spot): boolean {
  const dx = (spot.x - slab.x) / (slab.width / 2);
  const dz = (spot.z - slab.z) / (slab.depth / 2);
  return slab.round ? dx * dx + dz * dz <= 1 : Math.abs(dx) <= 1 && Math.abs(dz) <= 1;
}

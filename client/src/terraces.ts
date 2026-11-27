import type { TerraceView } from "./types";

type Spot = { x: number; z: number };

/** How far the steps round a terrace reach out from its top, in metres. */
export const stepsReach = 1;

const stepRise = 0.2;

/** A painted terrace (a carpet, a planter) lies this far above the stone it covers, so their faces never share a plane. */
const paintLift = 0.01;

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
  if (terrace.slope) {
    return [];
  }
  const count = Math.max(1, Math.round(terrace.height / stepRise));
  return Array.from({ length: count }, (_, index) => {
    const outward = (stepsReach * (count - 1 - index)) / count;
    return { x: terrace.x, z: terrace.z, width: terrace.width + 2 * outward, depth: terrace.depth + 2 * outward, top: (terrace.height * (index + 1)) / count + (terrace.colour ? paintLift : 0), colour: terrace.colour ?? undefined, round: terrace.round ?? undefined };
  });
}

/** How high the ground is at a spot: the top of the highest slab of any terrace under it, else 0. */
export function groundHeight(terraces: readonly TerraceView[], spot: Spot): number {
  let height = 0;
  for (const terrace of terraces) {
    if (terrace.slope) {
      height = Math.max(height, rampHeight(terrace, spot));
      continue;
    }
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

/** How high a ramp is at a spot: nothing outside it, else rising evenly from the ground at its low side to its height at the side it rises toward. */
export function rampHeight(terrace: TerraceView, spot: Spot): number {
  const dx = spot.x - terrace.x;
  const dz = spot.z - terrace.z;
  if (!terrace.slope || Math.abs(dx) > terrace.width / 2 || Math.abs(dz) > terrace.depth / 2) {
    return 0;
  }
  const along = { west: (terrace.width / 2 - dx) / terrace.width, east: (dx + terrace.width / 2) / terrace.width, north: (dz + terrace.depth / 2) / terrace.depth, south: (terrace.depth / 2 - dz) / terrace.depth }[terrace.slope];
  const base = terrace.base ?? 0;
  return base + (terrace.height - base) * along;
}

/** A ramp's top and its four sides as flat-shaded triangles (each face its own vertices): positions and indices, the top first. */
export function rampMesh(terrace: TerraceView): { positions: number[]; topIndices: number[]; sideIndices: number[] } {
  const x0 = terrace.x - terrace.width / 2;
  const x1 = terrace.x + terrace.width / 2;
  const z0 = terrace.z - terrace.depth / 2;
  const z1 = terrace.z + terrace.depth / 2;
  const at = (x: number, z: number): [number, number, number] => [x, rampHeight(terrace, { x, z }), z];
  const down = (x: number, z: number): [number, number, number] => [x, 0, z];
  const positions: number[] = [];
  const topIndices: number[] = [];
  const sideIndices: number[] = [];
  const quad = (into: number[], a: number[], b: number[], c: number[], d: number[]): void => {
    const first = positions.length / 3;
    positions.push(...a, ...b, ...c, ...d);
    into.push(first, first + 1, first + 2, first, first + 2, first + 3);
  };
  quad(topIndices, at(x0, z0), at(x0, z1), at(x1, z1), at(x1, z0));
  quad(sideIndices, down(x0, z0), at(x0, z0), at(x1, z0), down(x1, z0));
  quad(sideIndices, down(x1, z1), at(x1, z1), at(x0, z1), down(x0, z1));
  quad(sideIndices, down(x0, z1), at(x0, z1), at(x0, z0), down(x0, z0));
  quad(sideIndices, down(x1, z0), at(x1, z0), at(x1, z1), down(x1, z1));
  return { positions, topIndices, sideIndices };
}

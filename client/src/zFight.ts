import { slabs } from "./terraces";
import type { TerraceView } from "./types";

export interface Block {
  name: string;
  colour: string;
  /** Centre then size: x, y, z, width, height, depth. */
  box: [number, number, number, number, number, number];
}

export interface BackdropBox {
  shape: string;
  width?: number;
  height?: number;
  depth?: number;
  diameter?: number;
  at: number[];
  colour?: string;
  turn?: number[];
}

const planeTolerance = 0.002;
const overlapNeeded = 0.02;

/** The axis-aligned boxes among a room's backdrop parts: a turned part, a ball or a cylinder is left out. */
export function blocksOfParts(parts: readonly BackdropBox[]): Block[] {
  return parts.flatMap((part, index) => {
    if (part.shape !== "box" || (part.turn && part.turn.some((angle) => angle !== 0))) {
      return [];
    }
    const [x = 0, y = 0, z = 0] = part.at;
    return [{ name: `part ${index + 1} (box at ${x}, ${z})`, colour: part.colour ?? "#ffffff", box: [x, y, z, part.width ?? 1, part.height ?? 1, part.depth ?? 1] as Block["box"] }];
  });
}

function stoneColour(index: number, count: number): string {
  if (index === count - 1) {
    return "#c9b58e";
  }
  return index % 2 === 0 ? "#e3d8bd" : "#efe6d0";
}

/** The rectangular terraces' slabs, as the client stacks them. */
export function blocksOfTerraces(terraces: readonly TerraceView[]): Block[] {
  return terraces.flatMap((terrace, t) => {
    if (terrace.round) {
      return [];
    }
    return slabs(terrace).map((slab, index, all) => ({
      name: `terrace ${t + 1} step ${index + 1}`,
      colour: slab.colour ?? stoneColour(index, all.length),
      box: [slab.x, slab.top / 2, slab.z, slab.width, slab.top, slab.depth] as Block["box"],
    }));
  });
}

type Box = Block["box"];

const faceNames = [["west", "east"], ["bottom", "top"], ["south", "north"]] as const;

const low = (box: Box, axis: number): number => (box[axis] ?? 0) - (box[axis + 3] ?? 0) / 2;
const high = (box: Box, axis: number): number => (box[axis] ?? 0) + (box[axis + 3] ?? 0) / 2;

/** Whether two boxes overlap across the two axes other than this one, by more than a sliver. */
function overlapAcross(p: Box, q: Box, axis: number): boolean {
  return [0, 1, 2].filter((i) => i !== axis).every((i) => Math.min(high(p, i), high(q, i)) - Math.max(low(p, i), low(q, i)) > overlapNeeded);
}

/** The faces, on one axis, that two boxes show in one plane: [side, height of the plane]. A bottom face is hidden by whatever it stands on. */
function sharedFaces(p: Box, q: Box, axis: number): { name: string; at: number }[] {
  const shared: { name: string; at: number }[] = [];
  if (overlapAcross(p, q, axis)) {
    for (const side of [0, 1]) {
      const [planeP, planeQ] = side === 0 ? [low(p, axis), low(q, axis)] : [high(p, axis), high(q, axis)];
      if (Math.abs(planeP - planeQ) < planeTolerance && !(axis === 1 && side === 0)) {
        shared.push({ name: faceNames[axis]?.[side] ?? "", at: planeP });
      }
    }
  }
  return shared;
}

/** Pairs of faces that lie in one plane, face the same way, overlap, and are painted differently: a camera sees them flicker (z-fighting). A bottom face is hidden and left out. */
export function zFights(blocks: readonly Block[]): string[] {
  const found: string[] = [];
  blocks.forEach((a, index) => {
    for (const b of blocks.slice(index + 1)) {
      if (a.colour === b.colour) {
        continue;
      }
      for (const axis of [0, 1, 2]) {
        for (const face of sharedFaces(a.box, b.box, axis)) {
          found.push(`${a.name} and ${b.name} share a ${face.name} face at ${face.at.toFixed(2)}`);
        }
      }
    }
  });
  return found;
}

import { floorBoxes, type PlacedChunk } from "./overworld";
import type { Part } from "./props";

/** How far past a room's floor scenery still counts as the room's own (its walls, fence and near trees). */
const attachedReach = 2;

/** A part's body reaching more than this onto another room's floor is a hill lying across it. */
const intrusion = 1;

/** Whether a spot is within the attached reach (2 m, along each axis, so corners reach further) of a room's floor. */
export function isAttached(chunk: PlacedChunk, x: number, z: number): boolean {
  return covers(chunk, x, z, attachedReach);
}

/** Whether a spot on the ground lies on a placed room's floor (or its edge), <margin> metres beyond it. */
function covers(chunk: Pick<PlacedChunk, "x" | "z" | "width" | "depth">, x: number, z: number, margin = 0): boolean {
  return Math.abs(x - chunk.x) <= chunk.width / 2 + margin && Math.abs(z - chunk.z) <= chunk.depth / 2 + margin;
}

/** Whether a spot lies on the floor of a placed room other than <chunk>, or on a path out of it: scenery there would stand on that room. */
export function onAnotherRoom(world: PlacedChunk[] | undefined, chunk: PlacedChunk, x: number, z: number): boolean {
  return world?.some((other) => other !== chunk && floorBoxes(other).some((box) => covers(box, x, z))) ?? false;
}

/** Whether a part's body reaches more than a metre onto another placed room's floor, though its centre is off it: a big hill behind a room. */
export function intrudesOnAnotherRoom(world: PlacedChunk[] | undefined, chunk: PlacedChunk, part: Part): boolean {
  let reachX = 0;
  let reachZ = 0;
  if (part.shape === "box") {
    reachX = part.width / 2;
    reachZ = part.depth / 2;
  } else if (part.shape !== "model") {
    reachX = part.diameter / 2;
    reachZ = reachX;
  }
  const [x, , z] = part.at;
  return world?.some((other) => other !== chunk
    && Math.min(x + reachX, other.x + other.width / 2) - Math.max(x - reachX, other.x - other.width / 2) > intrusion
    && Math.min(z + reachZ, other.z + other.depth / 2) - Math.max(z - reachZ, other.z - other.depth / 2) > intrusion) ?? false;
}

/** A part's identity across rooms: shape, size, colour and spot to the centimetre, so float noise in where two rooms put it doesn't matter. */
function keyOf(part: Part): string {
  const round = (n: number) => Math.round(n * 100) / 100;
  return JSON.stringify(part, (_key, value: unknown) => (typeof value === "number" ? round(value) : value));
}

/**
 * The backdrop parts (in world metres) a room draws. Scenery on or just beside the room's floor (attached, within
 * 2 m along each axis) stays with its room unless it would stand on another. Distant scenery (hills, mountains, far
 * trees) belongs to the world: drawn only off every room's floor, along the outline of the world. Either way a part
 * is drawn once: <seen> remembers what has been. Parts of different shape, size or spot are different parts; two rooms
 * that generate one hill differently get two hills.
 */
export function sceneryFor(world: PlacedChunk[] | undefined, chunk: PlacedChunk, parts: Part[], seen: Set<string>): Part[] {
  if (!world) {
    return parts;
  }
  return parts.filter((part) => {
    const [x, , z] = part.at;
    if (onAnotherRoom(world, chunk, x, z) || intrudesOnAnotherRoom(world, chunk, part)) {
      return false;
    }
    const key = keyOf(part);
    if (seen.has(key)) {
      return false;
    }
    seen.add(key);
    return true;
  });
}

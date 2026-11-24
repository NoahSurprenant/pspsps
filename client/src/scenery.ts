import type { PlacedChunk } from "./overworld";
import { worldBounds } from "./overworld";
import type { Part } from "./props";

/** How far past a room's floor scenery still counts as the room's own (its walls, fence and near trees). */
const attachedReach = 2;

/** A part's body reaching more than this onto another room's floor is a hill lying across it. */
const intrusion = 1;

/** Whether a spot on the ground lies on a placed room's floor (or its edge), <margin> metres beyond it. */
function covers(chunk: PlacedChunk, x: number, z: number, margin = 0): boolean {
  return Math.abs(x - chunk.x) <= chunk.width / 2 + margin && Math.abs(z - chunk.z) <= chunk.depth / 2 + margin;
}

/** Whether a spot lies on the floor of a placed room other than <chunk>: scenery there would stand on that room. */
export function onAnotherRoom(world: PlacedChunk[] | undefined, chunk: PlacedChunk, x: number, z: number): boolean {
  return world?.some((other) => other !== chunk && covers(other, x, z)) ?? false;
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

/**
 * The backdrop parts (in world metres) a room draws. Attached scenery, on or just beside the room's floor, stays with
 * its room unless it would stand on another. Distant scenery (hills, mountains, far trees) belongs to the world: drawn
 * once (<seen> remembers what is), and only outside the outline of all the rooms, along the world's outer edge.
 */
export function sceneryFor(world: PlacedChunk[] | undefined, chunk: PlacedChunk, parts: Part[], seen: Set<string>): Part[] {
  if (!world) {
    return parts;
  }
  const { minX, maxX, minZ, maxZ } = worldBounds(world);
  return parts.filter((part) => {
    const [x, , z] = part.at;
    if (onAnotherRoom(world, chunk, x, z) || intrudesOnAnotherRoom(world, chunk, part)) {
      return false;
    }
    if (covers(chunk, x, z, attachedReach)) {
      return true;
    }
    if (x >= minX && x <= maxX && z >= minZ && z <= maxZ) {
      return false;
    }
    const key = JSON.stringify(part);
    if (seen.has(key)) {
      return false;
    }
    seen.add(key);
    return true;
  });
}

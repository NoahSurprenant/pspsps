import type { ChunkView, DoorView, PondView, PropView, RoomSnapshot } from "./types";

type Floor = Pick<ChunkView, "x" | "z" | "width" | "depth">;
type Spot = { x: number; z: number };

/** A chunk with everything in it moved to world metres: what the scene draws and the cats walk among. */
export interface PlacedChunk extends ChunkView {
  /** The chunk's own doors, props and pond, unmoved (metres from its middle): what its painters work from. */
  local: Pick<ChunkView, "pond" | "doors" | "props">;
}

export function place(chunk: ChunkView): PlacedChunk {
  const door = (d: DoorView): DoorView => ({ ...d, x: d.x + chunk.x, z: d.z + chunk.z });
  const prop = (p: PropView): PropView => ({ ...p, x: p.x + chunk.x, z: p.z + chunk.z });
  const pond = (p: PondView): PondView => ({ ...p, x: p.x + chunk.x, z: p.z + chunk.z });
  return {
    ...chunk,
    pond: chunk.pond && pond(chunk.pond),
    doors: chunk.doors.map(door),
    props: chunk.props.map(prop),
    local: { pond: chunk.pond, doors: chunk.doors, props: chunk.props },
  };
}

/** The spot of the world's floor nearest to a spot: itself when it is on a chunk, else the closest point of the closest chunk. */
export function clampToWorld(chunks: readonly Floor[], spot: Spot): Spot {
  let best = spot;
  let bestDistance = Infinity;
  for (const chunk of chunks) {
    const candidate = {
      x: Math.max(chunk.x - chunk.width / 2, Math.min(spot.x, chunk.x + chunk.width / 2)),
      z: Math.max(chunk.z - chunk.depth / 2, Math.min(spot.z, chunk.z + chunk.depth / 2)),
    };
    const distance = Math.hypot(candidate.x - spot.x, candidate.z - spot.z);
    if (distance < bestDistance) {
      best = candidate;
      bestDistance = distance;
    }
  }
  return best;
}

/** The chunk whose floor a spot is on; where chunks touch, the first. */
export function chunkAt<T extends Floor>(chunks: readonly T[], spot: Spot): T | undefined {
  return chunks.find((c) => Math.abs(spot.x - c.x) <= c.width / 2 && Math.abs(spot.z - c.z) <= c.depth / 2);
}

/** The box round every chunk's floor: where the ground under the world reaches. */
export function worldBounds(chunks: readonly Floor[]):
  { minX: number; maxX: number; minZ: number; maxZ: number } {
  return {
    minX: Math.min(...chunks.map((c) => c.x - c.width / 2)),
    maxX: Math.max(...chunks.map((c) => c.x + c.width / 2)),
    minZ: Math.min(...chunks.map((c) => c.z - c.depth / 2)),
    maxZ: Math.max(...chunks.map((c) => c.z + c.depth / 2)),
  };
}

/** The doors a cat can walk onto: a room's own, or every chunk's, in world metres. */
export function worldDoors(room: Pick<RoomSnapshot, "doors" | "chunks">): DoorView[] {
  return room.chunks?.length ? room.chunks.flatMap((chunk) => place(chunk).doors) : room.doors;
}

/** A keyboard step of 0.7 m in a direction, held to the world's floor. */
export function stepInWorld(chunks: readonly Floor[], from: Spot, dx: number, dz: number): Spot {
  const stride = 0.7 / Math.hypot(dx, dz);
  return clampToWorld(chunks, { x: from.x + dx * stride, z: from.z + dz * stride });
}

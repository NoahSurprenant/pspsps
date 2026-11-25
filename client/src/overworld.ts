import { groundHeight } from "./terraces";
import type { ChunkView, DoorView, PondView, PropView, RoomSnapshot, TerraceView } from "./types";

type Box = Pick<ChunkView, "x" | "z" | "width" | "depth">;
type Floor = Box & { paths?: ChunkView["paths"] };
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
  const terrace = (t: TerraceView): TerraceView => ({ ...t, x: t.x + chunk.x, z: t.z + chunk.z });
  return {
    ...chunk,
    pond: chunk.pond && pond(chunk.pond),
    doors: chunk.doors.map(door),
    props: chunk.props.map(prop),
    terraces: chunk.terraces?.map(terrace),
    local: { pond: chunk.pond, doors: chunk.doors, props: chunk.props },
  };
}

/** How high the ground is at a spot of the world: on a terrace, its steps, else flat. */
export function worldHeight(chunks: readonly PlacedChunk[], spot: Spot): number {
  return groundHeight(chunks.flatMap((chunk) => chunk.terraces ?? []), spot);
}

/** Every piece of a chunk's floor, in world metres: the room's own, then the paths out of it. */
export function floorBoxes(chunk: Floor): Box[] {
  return [chunk, ...(chunk.paths ?? []).map((p) => ({ x: chunk.x + p.x, z: chunk.z + p.z, width: p.width, depth: p.depth }))];
}

function onBox(box: Box, spot: Spot): boolean {
  return Math.abs(spot.x - box.x) <= box.width / 2 && Math.abs(spot.z - box.z) <= box.depth / 2;
}

/** The spot of the world's floor nearest to a spot: itself when it is on a chunk, else the closest point of the closest chunk. */
export function clampToWorld(chunks: readonly Floor[], spot: Spot): Spot {
  let best = spot;
  let bestDistance = Infinity;
  for (const box of chunks.flatMap(floorBoxes)) {
    const candidate = {
      x: Math.max(box.x - box.width / 2, Math.min(spot.x, box.x + box.width / 2)),
      z: Math.max(box.z - box.depth / 2, Math.min(spot.z, box.z + box.depth / 2)),
    };
    const distance = Math.hypot(candidate.x - spot.x, candidate.z - spot.z);
    if (distance < bestDistance) {
      best = candidate;
      bestDistance = distance;
    }
  }
  return best;
}

/** The chunk whose floor a spot is on; where chunks touch, the first; on a path, the chunk it leaves. */
export function chunkAt<T extends Floor>(chunks: readonly T[], spot: Spot): T | undefined {
  return chunks.find((c) => onBox(c, spot)) ?? chunks.find((c) => floorBoxes(c).some((box) => onBox(box, spot)));
}

/** The box round every chunk's floor and paths: where the ground under the world reaches. */
export function worldBounds(chunks: readonly Floor[]):
  { minX: number; maxX: number; minZ: number; maxZ: number } {
  const boxes = chunks.flatMap(floorBoxes);
  return {
    minX: Math.min(...boxes.map((c) => c.x - c.width / 2)),
    maxX: Math.max(...boxes.map((c) => c.x + c.width / 2)),
    minZ: Math.min(...boxes.map((c) => c.z - c.depth / 2)),
    maxZ: Math.max(...boxes.map((c) => c.z + c.depth / 2)),
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

/** The pond of the chunks (in world metres) nearest a spot, or undefined when none has one. */
export function nearestPond(chunks: readonly PlacedChunk[], spot: Spot): PondView | undefined {
  let best: PondView | undefined;
  let bestDistance = Infinity;
  for (const { pond } of chunks) {
    if (!pond) {
      continue;
    }
    const distance = Math.hypot(pond.x - spot.x, pond.z - spot.z);
    if (distance < bestDistance) {
      best = pond;
      bestDistance = distance;
    }
  }
  return best;
}

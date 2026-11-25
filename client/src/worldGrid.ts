/** The world grid of outdoor rooms: a room's place is its south-west corner in whole cells (Pspsps.Domain WorldPlace). */
export const CELL = 2;

export interface GridPlace { x: number; z: number }
export interface RoomPath { edge: "North" | "East" | "South" | "West"; offset: number; width: number; length: number }
export interface Placed { id: string; name: string; width: number; depth: number; place: GridPlace | null; paths?: RoomPath[] }

const isPlace = (value: unknown): value is GridPlace =>
  typeof value === "object" && value !== null && Number.isInteger((value as GridPlace).x) && Number.isInteger((value as GridPlace).z);

export function placedOf(id: string, layout: { name: string; width: number; depth: number; place?: unknown; paths?: unknown }): Placed {
  const placed: Placed = { id, name: layout.name, width: layout.width, depth: layout.depth, place: isPlace(layout.place) ? { x: layout.place.x, z: layout.place.z } : null };
  return Array.isArray(layout.paths) && layout.paths.length > 0 ? { ...placed, paths: layout.paths as RoomPath[] } : placed;
}

/** The floor of each path out of a placed room, in metres: south-west corner and size (Pspsps.Domain RoomPath.Floor). */
export function pathRects(room: Placed): { x: number; z: number; width: number; depth: number }[] {
  if (!room.place) return [];
  const [cx, cz] = [room.place.x * CELL + room.width / 2, room.place.z * CELL + room.depth / 2];
  return (room.paths ?? []).map((path) => {
    const [mx, mz, width, depth] = path.edge === "North" ? [cx + path.offset, cz + room.depth / 2 + path.length / 2, path.width, path.length]
      : path.edge === "South" ? [cx + path.offset, cz - room.depth / 2 - path.length / 2, path.width, path.length]
      : path.edge === "East" ? [cx + room.width / 2 + path.length / 2, cz + path.offset, path.length, path.width]
      : [cx - room.width / 2 - path.length / 2, cz + path.offset, path.length, path.width];
    return { x: mx - width / 2, z: mz - depth / 2, width, depth };
  });
}

const shares = (a: Placed, b: Placed): boolean => {
  if (!a.place || !b.place) return false;
  return a.place.x * CELL < b.place.x * CELL + b.width && b.place.x * CELL < a.place.x * CELL + a.width
    && a.place.z * CELL < b.place.z * CELL + b.depth && b.place.z * CELL < a.place.z * CELL + a.depth;
};

/** The IDs of placed rooms that share floor with another placed room (touching edges are fine). */
export function overlapping(rooms: readonly Placed[]): Set<string> {
  const found = new Set<string>();
  for (const [i, a] of rooms.entries()) {
    for (const b of rooms.slice(i + 1)) {
      if (shares(a, b)) {
        found.add(a.id);
        found.add(b.id);
      }
    }
  }
  return found;
}

export function withPlace<T extends object>(layout: T, place: GridPlace | null): T & { place: GridPlace | null } {
  return { ...layout, place };
}

/** The first spot, scanning east along each row, then north, where a room of this size fits among the placed ones. */
export function firstFreePlace(rooms: readonly Placed[], width: number, depth: number): GridPlace {
  const placed = rooms.filter((room) => room.place);
  const right = placed.reduce((most, room) => Math.max(most, (room.place?.x ?? 0) + room.width / CELL), 0);
  for (let z = 0; z <= right; z++) {
    for (let x = 0; x <= right; x++) {
      const candidate: Placed = { id: "", name: "", width, depth, place: { x, z } };
      if (!placed.some((room) => shares(room, candidate))) return { x, z };
    }
  }
  return { x: right, z: 0 };
}

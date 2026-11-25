import { describe, expect, it } from "vitest";
import { CELL, firstFreePlace, overlapping, pathRects, placedOf, withPlace, type Placed } from "./worldGrid";

const room = (id: string, width: number, depth: number, place: { x: number; z: number } | null): Placed => ({ id, name: id, width, depth, place });

describe("world grid", () => {
  it("reads a place from a layout, and ignores a layout without a usable one", () => {
    expect(placedOf("a", { name: "A", width: 8, depth: 6, place: { x: 2, z: 3 } })).toEqual({ ...room("a", 8, 6, { x: 2, z: 3 }), name: "A" });
    expect(placedOf("b", { name: "B", width: 8, depth: 6 }).place).toBeNull();
    expect(placedOf("c", { name: "C", width: 8, depth: 6, place: { x: "no" } }).place).toBeNull();
  });

  it("finds the rooms that share floor with another, and not rooms that only touch or are unplaced", () => {
    const rooms = [room("a", 8, 8, { x: 0, z: 0 }), room("b", 8, 8, { x: 4, z: 0 }), room("c", 8, 8, { x: 6, z: 2 }), room("d", 8, 8, null)];

    expect([...overlapping(rooms)].sort()).toEqual(["b", "c"]);
  });

  it("reads the paths of a layout and lays each out in metres from its room", () => {
    const placed = placedOf("a", { name: "A", width: 8, depth: 6, place: { x: 1, z: 1 }, paths: [{ edge: "East", offset: 1, width: 3, length: 6 }, { edge: "North", offset: 0, width: 2, length: 4 }] });

    expect(pathRects(placed)).toEqual([{ x: 10, z: 4.5, width: 6, depth: 3 }, { x: 5, z: 8, width: 2, depth: 4 }]);
    expect(pathRects(room("b", 8, 6, null))).toEqual([]);
    expect(placedOf("c", { name: "C", width: 8, depth: 6, paths: [] })).not.toHaveProperty("paths");
  });

  it("counts a path running over another room, or across another path, as sharing floor, and a path ending at a room as not", () => {
    const withPath = (id: string, x: number, z: number, length: number) => placedOf(id, { name: id, width: 8, depth: 8, place: { x, z }, paths: [{ edge: "East", offset: 0, width: 2, length }] });
    // a ends at b's west edge (4 cells = 8 m east of a's); c stands where a's longer path would run.
    expect([...overlapping([withPath("a", 0, 0, 8), room("b", 8, 8, { x: 8, z: 0 })])]).toEqual([]);
    expect([...overlapping([withPath("a", 0, 0, 20), room("b", 8, 8, { x: 8, z: 0 })])].sort()).toEqual(["a", "b"]);
    expect([...overlapping([withPath("a", 0, 0, 8), withPath("d", 4, 0, 8)])].sort()).toEqual(["a", "d"]);
  });

  it("sets and removes a place in a layout, keeping everything else", () => {
    const layout = { name: "A", width: 8, depth: 6, props: [1] };

    expect(withPlace(layout, { x: 1, z: 2 })).toEqual({ ...layout, place: { x: 1, z: 2 } });
    expect(withPlace({ ...layout, place: { x: 1, z: 2 } }, null)).toEqual({ ...layout, place: null });
  });

  it("puts a new room in the first free spot beside the others, on the grid", () => {
    const rooms = [room("a", 8, 8, { x: 0, z: 0 })];
    const spot = firstFreePlace(rooms, 8, 8);

    expect(spot).toEqual({ x: 8 / CELL, z: 0 });
    expect(firstFreePlace([], 8, 8)).toEqual({ x: 0, z: 0 });
  });
});

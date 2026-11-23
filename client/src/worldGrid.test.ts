import { describe, expect, it } from "vitest";
import { CELL, firstFreePlace, overlapping, placedOf, withPlace, type Placed } from "./worldGrid";

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

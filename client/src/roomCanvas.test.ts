import { describe, expect, it } from "vitest";
import { addProp, doorAtEdge, doorSpot, fromPixel, isLayout, moveTo, newRoomTemplate, pick, removePicked, toPixel } from "./roomCanvas";

const room = {
  ...newRoomTemplate("Loft"),
  props: [{ kind: "rug", spot: { x: 2, z: 1 }, radius: 1.5 }],
  doors: [{ id: "garden", edge: "East" as const, offset: 1, toRoom: "garden", toDoor: "loft" }],
};

const door0 = room.doors[0] ?? { edge: "East" as const, offset: 1 };

describe("room canvas geometry", () => {
  it("puts the back of the room at the top and east on the right, and goes back again", () => {
    expect(toPixel(room, 10, { x: -6, z: 5 })).toEqual({ px: 0, py: 0 });
    expect(toPixel(room, 10, { x: 6, z: -5 })).toEqual({ px: 120, py: 100 });
    const spot = fromPixel(room, 10, 80, 30);
    expect(toPixel(room, 10, spot)).toEqual({ px: 80, py: 30 });
  });

  it("stands a door on its edge and finds the edge a dragged door is nearest", () => {
    expect(doorSpot(room, door0)).toEqual({ x: 6, z: 1 });
    expect(doorAtEdge(room, { x: 5.8, z: 2 })).toEqual({ edge: "East", offset: 2 });
    expect(doorAtEdge(room, { x: 1, z: 4.9 })).toEqual({ edge: "North", offset: 1 });
    expect(doorAtEdge(room, { x: -9, z: 0 })).toEqual({ edge: "West", offset: 0 });
  });
});

describe("room canvas editing", () => {
  it("picks doors and the spawn before props, and nothing on empty floor", () => {
    expect(pick(room, { x: 6, z: 1 })).toEqual({ kind: "door", index: 0 });
    expect(pick(room, { x: 0.1, z: 0 })).toEqual({ kind: "spawn" });
    expect(pick(room, { x: 2.5, z: 1.5 })).toEqual({ kind: "prop", index: 0 });
    expect(pick(room, { x: -4, z: -4 })).toBeNull();
  });

  it("moves a prop inside the floor and a door along the edge, without changing the layout it was given", () => {
    const moved = moveTo(room, { kind: "prop", index: 0 }, { x: 40, z: -1 });
    const door = moveTo(room, { kind: "door", index: 0 }, { x: 0, z: 4.9 });

    expect(moved.props[0]?.spot).toEqual({ x: 6, z: -1 });
    expect(door.doors[0]).toMatchObject({ edge: "North", offset: 0 });
    expect(room.props[0]?.spot).toEqual({ x: 2, z: 1 });
  });

  it("adds and removes props, and keeps the rest of the layout", () => {
    const more = addProp({ ...room, look: "winter" }, "tree", { x: 1.234, z: 2 });

    expect(more.props).toHaveLength(2);
    expect(more.props[1]).toMatchObject({ kind: "tree", spot: { x: 1.23, z: 2 } });
    expect(more.look).toBe("winter");
    expect(removePicked(more, { kind: "prop", index: 0 }).props).toHaveLength(1);
    expect(removePicked(room, { kind: "door", index: 0 }).doors).toHaveLength(0);
  });

  it("knows a layout from other JSON", () => {
    expect(isLayout(room)).toBe(true);
    expect(isLayout({ width: 1 })).toBe(false);
    expect(isLayout(null)).toBe(false);
  });
});

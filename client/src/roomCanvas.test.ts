import { describe, expect, it } from "vitest";
import { History, addProp, backDoor, newDoor, setPond, setProp, setStorefront, doorAtEdge, doorSpot, fromPixel, isLayout, moveTo, newRoomTemplate, pick, removePicked, toPixel } from "./roomCanvas";
import { propProblems, propsInTheWay } from "./roomClearance";

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

describe("room canvas aids", () => {
  it("undoes and redoes, and forgets the redo after a new change", () => {
    const history = new History(1);
    history.push(2);
    history.push(3);

    expect([history.undo(), history.undo(), history.undo()]).toEqual([2, 1, 1]);
    expect(history.canUndo).toBe(false);
    expect(history.redo()).toBe(2);
    history.push(9);
    expect([history.canRedo, history.value]).toEqual([false, 9]);
  });

  it("changes a prop's size and facing, the pond and the storefront without touching the original", () => {
    const sized = setProp(room, 0, { radius: 2, facing: 0.25 });
    const pond = setPond(room, { center: { x: -2, z: 2 }, radius: 1.5 });

    expect(sized.props[0]).toMatchObject({ radius: 2, facing: 0.25 });
    expect(room.props[0]?.radius).toBe(1.5);
    expect(pond.pond?.radius).toBe(1.5);
    expect(setPond(pond, null).pond).toBeNull();
    expect(setStorefront(room, { width: 12, depth: 6, front: "South" }).exterior?.front).toBe("South");
    expect(pick(pond, { x: -2.2, z: 2 })).toEqual({ kind: "pond" });
    expect(moveTo(pond, { kind: "pond" }, { x: 1, z: 1 }).pond?.center).toEqual({ x: 1, z: 1 });
  });

  it("makes a door with the one that leads back: opposite edge, same offset, each pointing at the other", () => {
    const door = newDoor(room, "East", "garden", "loft");
    const back = backDoor({ ...door, offset: 2 }, "loft");

    expect(door).toEqual({ id: "garden-2", edge: "East", offset: 0, toRoom: "garden", toDoor: "loft" });
    expect(back).toEqual({ id: "loft", edge: "West", offset: 2, toRoom: "loft", toDoor: "garden-2" });
    expect(newDoor({ ...room, doors: [] }, "North", "garden", "loft").id).toBe("garden");
  });
});

describe("prop clearance, as the server judges it", () => {
  const props = (...list: { kind: string; x: number; z: number; radius: number }[]) =>
    ({ ...room, doors: [], props: list.map((p) => ({ kind: p.kind, spot: { x: p.x, z: p.z }, radius: p.radius })) });

  it("is happy with a prop off to the side", () => {
    expect(propProblems(props({ kind: "tree", x: -4, z: -3, radius: 1 }), 0)).toEqual([]);
  });

  it("says when a prop is on the spawn point, partly off the floor, or on another prop", () => {
    const layout = props({ kind: "tree", x: 0, z: 0, radius: 1 }, { kind: "bench", x: 5.5, z: -4.5, radius: 1 }, { kind: "rug", x: -4, z: 3, radius: 1 }, { kind: "pot", x: -4, z: 3.5, radius: 0.5 });

    expect(propProblems(layout, 0)).toContain("is on the spawn point");
    expect(propProblems(layout, 1)).toContain("is partly off the floor");
    expect(propProblems(layout, 2)).toContain("overlaps the pot");
    expect(propsInTheWay(layout)).toEqual([0, 1, 2, 3]);
  });

  it("keeps clear of a door, the way in by it, the pond and the walk between", () => {
    const withDoor = { ...props({ kind: "tree", x: 4.2, z: 1, radius: 0.5 }), doors: room.doors };
    const withPond = { ...props({ kind: "tree", x: -3, z: 2, radius: 0.5 }), pond: { center: { x: -3, z: 3 }, radius: 1 } };

    expect(propProblems(withDoor, 0)).toContain("is on the door garden");
    expect(propProblems(withPond, 0)).toContain("is in the pond or where cats fish from");
    expect(propProblems({ ...props({ kind: "tree", x: 2, z: 0, radius: 0.5 }), doors: room.doors }, 0)).toContain("is on the walk from the spawn point to the door garden");
  });
});

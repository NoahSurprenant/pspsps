import { describe, expect, it } from "vitest";
import { lookOf } from "./looks";
import type { Floorplan } from "./paint";

const plans: Record<string, Floorplan> = {
  "town-square": { width: 24, depth: 16, pond: { x: -7, z: -3, radius: 2.5 }, doors: [] },
  cafe: { width: 14, depth: 10, pond: null, doors: [] },
};

describe("lookOf", () => {
  it("gives Town Square and the café each their own look", () => {
    const [square, cafe] = [lookOf("town-square"), lookOf("cafe")];

    expect(square.paintFloor).toBeDefined();
    expect(cafe.paintFloor).toBeDefined();
    expect(square.paintFloor).not.toBe(cafe.paintFloor);
    expect(square.sky).not.toBe(cafe.sky);
  });

  it("gives a room it has no look for a plain floor and wall, and no backdrop", () => {
    const look = lookOf("attic");

    expect(look.paintFloor).toBeUndefined();
    expect(look.backdrop({ width: 14, depth: 10, pond: null, doors: [] }, 4)).toEqual([]);
  });

  // The backdrop is scenery: nothing in it stands where cats walk, or hangs low enough over the floor to hide one.
  it.each(Object.entries(plans))("keeps %s's backdrop off the floor, or above the cats' heads", (room, plan) => {
    for (const part of lookOf(room).backdrop(plan, 4)) {
      const [x, y, z] = part.at;
      const [halfX, halfZ] = part.shape === "box" ? [part.width / 2, part.depth / 2] : [part.diameter / 2, part.diameter / 2];
      const height = part.shape === "ball" ? (part.height ?? part.diameter) : part.height;
      const overFloor = Math.abs(x) - halfX < plan.width / 2 && Math.abs(z) - halfZ < plan.depth / 2;
      if (overFloor) {
        expect(y - height / 2, `${part.shape} at ${part.at.join(", ")}`).toBeGreaterThan(2);
      }
    }
  });
});

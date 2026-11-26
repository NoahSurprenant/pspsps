import { describe, expect, it } from "vitest";
import { followBall, onFloor, slide } from "./iceWalk";

describe("a cat on ice", () => {
  it("gets up to speed slowly, not at once", () => {
    const first = slide({ x: 0, z: 0 }, { x: 0, z: 0 }, { x: 10, z: 0 }, 4, 1 / 60);
    expect(first.x).toBeGreaterThan(0);
    expect(first.x).toBeLessThan(1);
    expect(first.z).toBe(0);
  });

  it("keeps sliding past a spot it was heading for, then is still", () => {
    const velocity = slide({ x: 4, z: 0 }, { x: 5, z: 0 }, { x: 5, z: 0 }, 4, 1 / 60);
    expect(velocity.x).toBeGreaterThan(3.5);
    let v = { x: 0.04, z: 0 };
    v = slide(v, { x: 5, z: 0 }, { x: 5, z: 0 }, 4, 1 / 60);
    expect(v).toEqual({ x: 0, z: 0 });
  });

  it("slows on the way in, so it does not overshoot far", () => {
    let position = { x: 0, z: 0 };
    let v = { x: 0, z: 0 };
    let farthest = 0;
    for (let step = 0; step < 60 * 20; step++) {
      v = slide(v, position, { x: 6, z: 0 }, 4, 1 / 60);
      position = { x: position.x + v.x / 60, z: position.z + v.z / 60 };
      farthest = Math.max(farthest, position.x);
    }
    expect(farthest).toBeLessThan(7);
    expect(position.x).toBeCloseTo(6, 0);
  });
});

describe("a room's floor", () => {
  it("holds the spots inside its size round its middle", () => {
    const room = { x: 100, z: 50, width: 24, depth: 16 };
    expect(onFloor({ x: 111, z: 57 }, room)).toBe(true);
    expect(onFloor({ x: 113, z: 50 }, room)).toBe(false);
    expect(onFloor({ x: 100, z: 59 }, room)).toBe(false);
  });
});

describe("a ball being followed", () => {
  const told = { kind: "puck", x: 10, z: 0, velocityX: 4, velocityZ: 0 };

  it("is drawn ahead of where it was told to be, by its speed", () => {
    expect(followBall({ x: 10.4, z: 0 }, told, 0.1, 1)).toEqual({ x: 10.4, z: 0 });
  });

  it("eases a drawn ball towards where it should be", () => {
    const next = followBall({ x: 0, z: 0 }, told, 0, 1 / 60);
    expect(next.x).toBeGreaterThan(0);
    expect(next.x).toBeLessThan(10);
  });
});

import { describe, expect, it } from "vitest";
import type { PlacedChunk } from "./overworld";
import { fadeAt, fadeAtSides, groundBlendOf, groundPatchHeight, groundPatchOf, shoreFeathers } from "./scene";

describe("fadeAt", () => {
  it("shows all of a room's ground over the room and none at the patch's edge, fading between", () => {
    // A patch 40 m wider each side than the room: the room is the middle, and the fade is 0.25 of the patch each way.
    expect(fadeAt(0.5, 0.5, 0.25, 0.25)).toBe(255);
    expect(fadeAt(0.25, 0.5, 0.25, 0.25)).toBe(255);
    expect(fadeAt(0, 0.5, 0.25, 0.25)).toBe(0);
    expect(fadeAt(0.5, 1, 0.25, 0.25)).toBe(0);
    expect(fadeAt(0.125, 0.5, 0.25, 0.25)).toBe(128);
  });

  it("fades by the nearer edge across the two directions", () => {
    expect(fadeAt(0.5, 0.0625, 0.25, 0.25)).toBe(64);
    expect(fadeAt(0.0625, 0.0625, 0.25, 0.25)).toBe(64);
  });
});

describe("groundBlendOf", () => {
  it("blends a room's ground into its neighbour's over a short way, but the ground under the whole world out over far more, so it ends at the horizon, not as a visible flat edge", () => {
    expect(groundBlendOf(false)).toBe(40);
    expect(groundBlendOf(true)).toBeGreaterThanOrEqual(100);
  });
});

describe("groundPatchOf", () => {
  const room = (id: string, x: number): PlacedChunk => ({ roomId: id, x, z: 0, width: 20, depth: 10 }) as PlacedChunk;
  const [base, other] = [room("a", 0), room("b", 20)];

  it("gives the world's base ground a patch over every room, with the long fade; another room's ground a patch round itself with the short one", () => {
    const whole = groundPatchOf(base, base, [base, other]);
    const beside = groundPatchOf(other, base, [base, other]);

    expect(whole).toMatchObject({ wholeWorld: true, chunk: { x: 10, z: 0, width: 40, depth: 10 } });
    expect(beside).toMatchObject({ wholeWorld: false, chunk: { x: 20, width: 20 } });
  });

  it("lays the world's patch below every room's, and each room's above the last, so no fade covers a neighbour's ground", () => {
    expect(groundPatchHeight(true, 0)).toBeLessThan(groundPatchHeight(false, 0));
    expect(groundPatchHeight(false, 1)).toBeGreaterThan(groundPatchHeight(false, 0));
  });
});

describe("shoreFeathers", () => {
  const room = (id: string, x: number, z: number): PlacedChunk => ({ roomId: id, x, z, width: 28, depth: 16 }) as PlacedChunk;
  const sea = room("sea", 0, 0);

  it("fades the full way where no other ground is beside the room", () => {
    expect(shoreFeathers(sea, [], 40)).toEqual({ west: 40, east: 40, south: 40, north: 40 });
  });

  it("stops the fade at a land room beside it: the gap to its edge, on that side only", () => {
    const land = room("land", 34, 0);
    expect(shoreFeathers(sea, [land], 40)).toEqual({ west: 40, east: 6, south: 40, north: 40 });
    expect(shoreFeathers(sea, [room("north", 0, 22)], 40)).toMatchObject({ north: 6, east: 40 });
  });

  it("ignores a room that is not beside it: across a corner, or beyond the fade", () => {
    expect(shoreFeathers(sea, [room("diagonal", 34, 22)], 40)).toEqual({ west: 40, east: 40, south: 40, north: 40 });
    expect(shoreFeathers(sea, [room("far", 90, 0)], 40).east).toBe(40);
  });
});

describe("fadeAtSides", () => {
  it("fades each side by its own width", () => {
    const feather = { west: 0.5, east: 0.1, south: 0.1, north: 0.1 };
    expect(fadeAtSides(0.25, 0.5, feather)).toBe(128);
    expect(fadeAtSides(0.95, 0.5, feather)).toBe(128);
    expect(fadeAtSides(0.5, 0.5, feather)).toBe(255);
  });
});

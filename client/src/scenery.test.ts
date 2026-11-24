import { describe, expect, it } from "vitest";
import { isAttached, sceneryFor } from "./scenery";

const room = (x: number, z: number) => ({ roomId: `r${x}-${z}`, x, z, width: 20, depth: 14 }) as never;
const part = (x: number, z: number, diameter = 2) => ({ shape: "ball" as const, diameter, at: [x, 0, z] as [number, number, number], colour: "#fff" });

// Two rooms stacked north-south: the south one spans z -7..7, the north one 7..21; the world's edge is z 21 (north) and -7 (south).
const south = room(0, 0);
const north = room(0, 14);
const world = [south, north];

describe("sceneryFor", () => {
  it("draws everything for a room alone", () => {
    const parts = [part(0, 3), part(0, 60)];
    expect(sceneryFor(undefined, south, parts, new Set())).toEqual(parts);
  });

  it("keeps a room's attached scenery (on or just beside its floor) with that room", () => {
    const tree = part(8, 0);
    expect(sceneryFor(world, south, [tree], new Set())).toEqual([tree]);
  });

  it("drops attached scenery that stands on another room's floor", () => {
    expect(sceneryFor(world, south, [part(0, 10)], new Set())).toEqual([]);
  });

  it("draws distant scenery (a hill far behind the room) only past the world's outer edge", () => {
    const behindNorth = part(0, 40);
    const behindSouthOutsideTheWorld = part(0, -40);
    expect(sceneryFor(world, north, [behindNorth], new Set())).toEqual([behindNorth]);
    expect(sceneryFor(world, south, [behindSouthOutsideTheWorld], new Set())).toEqual([behindSouthOutsideTheWorld]);
  });

  it("draws distant scenery in the notch of an L-shaped world, once", () => {
    const l = [room(0, 0), room(20, 0), room(0, 14)];
    const hill = part(20, 12);
    const seen = new Set<string>();
    expect(l.flatMap((chunk) => sceneryFor(l, chunk as never, [hill], seen))).toEqual([hill]);
  });

  it("does not draw distant scenery over another room's floor", () => {
    const l = [room(0, 0), room(20, 0), room(0, 14)];
    expect(sceneryFor(l, l[0] as never, [part(20, 3)], new Set())).toEqual([]);
  });

  it("draws a part within reach of two rooms (a gap under 4 m) once", () => {
    const gap = [room(0, 0), room(24, 0)];
    const tree = part(12, 0);
    const seen = new Set<string>();
    expect(gap.flatMap((chunk) => sceneryFor(gap, chunk as never, [tree], seen))).toEqual([tree]);
  });

  it("matches the same hill from two rooms despite float noise in its position", () => {
    const seen = new Set<string>();
    expect(sceneryFor(world, north, [part(0.1 + 0.2, 40)], seen)).toHaveLength(1);
    expect(sceneryFor(world, south, [part(0.3, 40)], seen)).toEqual([]);
  });

  it("counts scenery as attached up to 2 m from the floor along each axis, not beyond", () => {
    expect(isAttached(south, 12, 0)).toBe(true);
    expect(isAttached(south, 12.01, 0)).toBe(false);
    expect(isAttached(south, 0, 9)).toBe(true);
    expect(isAttached(south, 0, 9.01)).toBe(false);
  });

  it("draws a hill that two rooms both list once", () => {
    const hill = part(0, 40);
    const seen = new Set<string>();
    expect(sceneryFor(world, north, [hill], seen)).toEqual([hill]);
    expect(sceneryFor(world, south, [{ ...hill }], seen)).toEqual([]);
  });
});

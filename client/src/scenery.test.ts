import { describe, expect, it } from "vitest";
import { sceneryFor } from "./scenery";

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

  it("does not draw distant scenery inside the world's outline (a notch of an L-shaped world)", () => {
    const l = [room(0, 0), room(20, 0), room(0, 14)];
    expect(sceneryFor(l, l[1] as never, [part(20, 12)], new Set())).toEqual([]);
  });

  it("draws a hill that two rooms both list once", () => {
    const hill = part(0, 40);
    const seen = new Set<string>();
    expect(sceneryFor(world, north, [hill], seen)).toEqual([hill]);
    expect(sceneryFor(world, south, [{ ...hill }], seen)).toEqual([]);
  });
});

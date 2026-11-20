import { describe, expect, it } from "vitest";
import { sceneryTarget, stepInRoom, walkToEdge } from "./walking";

const room = { width: 20, depth: 12 };

describe("clicking past the room", () => {
  it("keeps a target on the floor", () => {
    expect(walkToEdge({ x: 1, z: 2 }, { x: 4, z: -3 }, room)).toEqual({ x: 4, z: -3 });
  });

  it("stops at the first edge along the walk", () => {
    expect(walkToEdge({ x: 2, z: -2 }, { x: 18, z: 10 }, room)).toEqual({ x: 10, z: 4 });
    expect(walkToEdge({ x: 0, z: 0 }, { x: -2, z: -8 }, room)).toEqual({ x: -1.5, z: -6 });
  });

  it("can move along an edge or back into the room", () => {
    expect(walkToEdge({ x: 10, z: 0 }, { x: 10, z: 4 }, room)).toEqual({ x: 10, z: 4 });
    expect(walkToEdge({ x: 10, z: 0 }, { x: 8, z: 0 }, room)).toEqual({ x: 8, z: 0 });
  });

  it("projects scenery and sky clicks towards the room", () => {
    const origin = { x: 0, y: 10, z: -10 };
    const from = { x: 0, z: 0 };
    expect(sceneryTarget(from, { origin, direction: { x: 0, y: -1, z: 2 } }, room)).toEqual({ x: 0, z: 6 });
    expect(sceneryTarget(from, { origin, direction: { x: 0.25, y: 0.2, z: 1 } }, room)).toEqual({ x: 4, z: 6 });
  });
});

describe("keyboard steps", () => {
  it("takes equally long diagonal steps and slides along the edge", () => {
    const diagonal = stepInRoom({ x: 0, z: 0 }, 1, 1, room);
    expect(Math.hypot(diagonal.x, diagonal.z)).toBeCloseTo(0.7);
    expect(stepInRoom({ x: -10, z: 0 }, -1, 1, room)).toEqual({ x: -10, z: diagonal.z });
  });
});

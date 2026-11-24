import { describe, expect, it } from "vitest";
import { chunkAt, clampToWorld, nearestPond, place, stepInWorld, worldBounds, worldDoors } from "./overworld";
import type { ChunkView } from "./types";

function chunk(roomId: string, x: number, z: number, width = 20, depth = 10): ChunkView {
  return {
    roomId, name: roomId, x, z, width, depth, look: null,
    pond: { x: 1, z: 2, radius: 3 },
    doors: [{ id: "d", x: 4, z: 5, radius: 1, toRoomId: "x", toRoomName: "X" }],
    props: [{ kind: "tree", x: -1, z: -2, radius: 1, facing: 0 }],
  };
}

describe("overworld", () => {
  it("moves a chunk's doors, props and pond to world metres and keeps the local ones", () => {
    const placed = place(chunk("a", 100, 50));
    expect(placed.doors[0]).toMatchObject({ x: 104, z: 55 });
    expect(placed.props[0]).toMatchObject({ x: 99, z: 48 });
    expect(placed.pond).toMatchObject({ x: 101, z: 52, radius: 3 });
    expect(placed.local.doors[0]).toMatchObject({ x: 4, z: 5 });
  });

  it("clamps to the nearest point of the nearest chunk", () => {
    const chunks = [chunk("a", 0, 0), chunk("b", 20, 0)];
    expect(clampToWorld(chunks, { x: 5, z: 2 })).toEqual({ x: 5, z: 2 });
    expect(clampToWorld(chunks, { x: 15, z: 2 })).toEqual({ x: 15, z: 2 });
    expect(clampToWorld(chunks, { x: 40, z: 0 })).toEqual({ x: 30, z: 0 });
    expect(clampToWorld(chunks, { x: 0, z: -50 })).toEqual({ x: 0, z: -5 });
  });

  it("finds the chunk a spot is on, and none off the floor", () => {
    const chunks = [chunk("a", 0, 0), chunk("b", 20, 0)];
    expect(chunkAt(chunks, { x: 12, z: 0 })?.roomId).toBe("b");
    expect(chunkAt(chunks, { x: 0, z: 30 })).toBeUndefined();
  });

  it("bounds every floor", () => {
    expect(worldBounds([chunk("a", 0, 0), chunk("b", 20, 10)])).toEqual({ minX: -10, maxX: 30, minZ: -5, maxZ: 15 });
  });
});

describe("overworld walking", () => {
  const chunks = [chunk("a", 0, 0), chunk("b", 20, 0)];

  it("lists every chunk's doors in world metres, and a room's own doors as they are", () => {
    const own = [{ id: "own", x: 1, z: 1, radius: 1, toRoomId: "x", toRoomName: "X" }];
    expect(worldDoors({ doors: own, chunks: null })).toBe(own);
    expect(worldDoors({ doors: own, chunks }).map((d) => [d.x, d.z])).toEqual([[4, 5], [24, 5]]);
  });

  it("steps across the edge between chunks, and stops at the edge of the world", () => {
    expect(stepInWorld(chunks, { x: 9.9, z: 0 }, 1, 0)).toEqual({ x: 10.6, z: 0 });
    expect(stepInWorld(chunks, { x: 29.9, z: 0 }, 1, 0)).toEqual({ x: 30, z: 0 });
  });
});

describe("nearestPond", () => {
  it("picks the pond closest to the spot, in world metres, and none when no chunk has one", () => {
    const west = place(chunk("west", 0, 0));
    const east = place(chunk("east", 100, 0));
    expect(nearestPond([west, east], { x: 90, z: 0 })).toMatchObject({ x: 101 });
    expect(nearestPond([west, east], { x: 5, z: 0 })).toMatchObject({ x: 1 });
    expect(nearestPond([place({ ...chunk("dry", 0, 0), pond: null })], { x: 0, z: 0 })).toBeUndefined();
  });
});

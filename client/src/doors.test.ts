import { describe, expect, it } from "vitest";
import { DoorWalks, doorAt, pathEdge } from "./doors";
import type { DoorView } from "./types";

const cafe: DoorView = { id: "cafe", x: 7, z: 7, radius: 0.8, toRoomId: "cafe", toRoomName: "Cat Café" };

describe("doorAt", () => {
  it("finds the door a spot is on, edge included", () => {
    expect(doorAt([cafe], 7, 7)).toBe(cafe);
    expect(doorAt([cafe], 7.8, 7)).toBe(cafe);
    expect(doorAt([cafe], 7.6, 7.6)).toBeUndefined();
    expect(doorAt([cafe], 0, 4)).toBeUndefined();
    expect(doorAt([], 7, 7)).toBeUndefined();
  });
});

describe("pathEdge", () => {
  const square = { width: 24, depth: 16 };

  it("extends each outdoor path to its nearest edge", () => {
    expect(pathEdge({ x: -11, z: 3.3 }, square)).toEqual({ x: -12, z: 3.3 });
    expect(pathEdge({ x: 6.4, z: -7 }, square)).toEqual({ x: 6.4, z: -8 });
  });
});

describe("DoorWalks", () => {
  it("goes through with the latest walk only", () => {
    const walks = new DoorWalks();
    const first = walks.start();
    const second = walks.start();

    expect(walks.isGoing(first)).toBe(false);
    expect(walks.isGoing(second)).toBe(true);
  });

  it("is called off by anything else the cat does", () => {
    const walks = new DoorWalks();
    const walk = walks.start();

    walks.callOff();

    expect(walks.isGoing(walk)).toBe(false);
  });
});

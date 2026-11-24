import { describe, expect, it } from "vitest";
import { stackLevels } from "./scene";

describe("stackLevels", () => {
  it("leaves names that don't touch on the first line", () => {
    expect(stackLevels([{ x: 0, y: 100, width: 80 }, { x: 200, y: 100, width: 80 }])).toEqual([0, 0]);
  });

  it("lifts a name that overlaps one to its left, whatever order they come in", () => {
    expect(stackLevels([{ x: 50, y: 100, width: 90 }, { x: 0, y: 100, width: 90 }])).toEqual([1, 0]);
  });

  it("lifts a third name over both when all three overlap", () => {
    expect(stackLevels([{ x: 0, y: 100, width: 90 }, { x: 40, y: 100, width: 90 }, { x: 80, y: 100, width: 90 }])).toEqual([0, 1, 2]);
  });

  it("doesn't stack names on different lines of the screen", () => {
    expect(stackLevels([{ x: 0, y: 100, width: 90 }, { x: 10, y: 200, width: 90 }])).toEqual([0, 0]);
  });
});

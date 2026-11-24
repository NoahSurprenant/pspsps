import { describe, expect, it } from "vitest";
import { keepOnScreen, onAnotherRoom, outlineStrips, stackLevels } from "./scene";

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

describe("keepOnScreen", () => {
  it("leaves a name that fits where it is", () => {
    expect(keepOnScreen(300, 80, 600)).toBe(0);
  });

  it("moves a name cut by the left edge right, and one cut by the right edge left", () => {
    expect(keepOnScreen(10, 80, 600)).toBe(30);
    expect(keepOnScreen(590, 80, 600)).toBe(-30);
  });

  it("leaves a name wider than the canvas alone", () => {
    expect(keepOnScreen(10, 700, 600)).toBe(0);
  });
});

describe("onAnotherRoom", () => {
  const room = (x: number, z: number) => ({ roomId: `r${x}-${z}`, x, z, width: 20, depth: 10 }) as never;
  const south = room(0, 0);
  const north = room(0, 10);

  it("is true for a spot on a neighbour's floor, so scenery isn't drawn over it", () => {
    expect(onAnotherRoom([south, north], south, 5, 10)).toBe(true);
  });

  it("is false for a spot on the room's own floor or off every floor", () => {
    expect(onAnotherRoom([south, north], south, 5, 0)).toBe(false);
    expect(onAnotherRoom([south, north], south, 5, 40)).toBe(false);
  });

  it("is false when there is no open world", () => {
    expect(onAnotherRoom(undefined, south, 5, 10)).toBe(false);
  });
});

describe("outlineStrips", () => {
  const room = { x: 10, z: 20, width: 24, depth: 16 } as never;

  it("lies inside the room's exact bounds, so a neighbouring floor can't cover it", () => {
    for (const strip of outlineStrips(room, 0.5)) {
      expect(strip.x - strip.width / 2).toBeGreaterThanOrEqual(10 - 12);
      expect(strip.x + strip.width / 2).toBeLessThanOrEqual(10 + 12);
      expect(strip.z - strip.depth / 2).toBeGreaterThanOrEqual(20 - 8);
      expect(strip.z + strip.depth / 2).toBeLessThanOrEqual(20 + 8);
    }
  });

  it("has one strip along each of the four edges", () => {
    const strips = outlineStrips(room, 0.5);
    expect(strips).toHaveLength(4);
    expect(strips).toContainEqual({ x: 10, z: 20 + 8 - 0.25, width: 24, depth: 0.5 });
    expect(strips).toContainEqual({ x: 10, z: 20 - 8 + 0.25, width: 24, depth: 0.5 });
    expect(strips).toContainEqual({ x: 10 + 12 - 0.25, z: 20, width: 0.5, depth: 15 });
    expect(strips).toContainEqual({ x: 10 - 12 + 0.25, z: 20, width: 0.5, depth: 15 });
  });
});

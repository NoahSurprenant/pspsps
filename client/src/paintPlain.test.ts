import { describe, expect, it } from "vitest";
import type { Brush, Floorplan } from "./paint";
import { paintBrickWall, paintChecker, paintPanelledWall, paintPlanks, paintTiles, shade } from "./paintPlain";

const plan: Floorplan = { width: 8, depth: 6, pond: null, doors: [] };

function recordingBrush(): { brush: Brush; colours: Set<string>; calls: string[] } {
  const colours = new Set<string>();
  const calls: string[] = [];
  const state: Record<string | symbol, unknown> = {};
  const brush = new Proxy(state, {
    get: (target, key) => key in target ? target[key] : (...args: unknown[]) => {
      calls.push(`${String(key)}(${args.join(",")})`);
    },
    set: (target, key, value) => {
      if (key === "fillStyle" || key === "strokeStyle") colours.add(String(value).toLowerCase());
      target[key] = value;
      return true;
    },
  }) as unknown as Brush;
  return { brush, colours, calls };
}

describe("shade", () => {
  it("darkens toward black and lightens toward white", () => {
    expect(shade("#c88040", -0.5)).toBe("#644020");
    expect(shade("#000000", 1)).toBe("#ffffff");
    expect(shade("#c88040", 0)).toBe("#c88040");
  });
});

describe("the plain painters", () => {
  it.each([
    ["planks", (b: Brush) => paintPlanks(b, plan, 32, { colour: "#aa8855" })],
    ["tiles", (b: Brush) => paintTiles(b, plan, 32, { colour: "#aa8855" })],
    ["checker", (b: Brush) => paintChecker(b, plan, 32, { colour: "#aa8855" })],
    ["panelled", (b: Brush) => paintPanelledWall(b, 8, 4, 32, { colour: "#aa8855" })],
    ["bricks", (b: Brush) => paintBrickWall(b, 8, 4, 32, { colour: "#aa8855" })],
  ])("paints %s in the room's own colour", (_name, paint) => {
    const { brush, colours, calls } = recordingBrush();

    paint(brush);

    expect(colours.has("#aa8855")).toBe(true);
    expect(calls.length).toBeGreaterThan(0);
  });

  it("uses the accent a room gives, and a darker shade of its colour where it gives none", () => {
    const given = recordingBrush();
    const left = recordingBrush();

    paintBrickWall(given.brush, 8, 4, 32, { colour: "#aa8855", accent: "#123456" });
    paintBrickWall(left.brush, 8, 4, 32, { colour: "#aa8855" });

    expect(given.colours.has("#123456")).toBe(true);
    expect(left.colours.has(shade("#aa8855", -0.18))).toBe(true);
  });
});

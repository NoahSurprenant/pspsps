import { describe, expect, it } from "vitest";
import { lookOf, setLooks, type LooksData } from "./looks";
import { mostRects, paintSteps, setRecipes, type PaintStep } from "./recipes";
import { newPainterTemplate, newWildlifeTemplate, recipeBody } from "./roomsAdmin";

function recorder() {
  const calls: string[] = [];
  const brush = new Proxy({ fillStyle: "", strokeStyle: "", lineWidth: 0 } as Record<string, unknown>, {
    get: (target, name: string) => (name in target ? target[name] : (...args: unknown[]) => calls.push(`${name}:${String(target.fillStyle)}:${args.join(",")}`)),
    set: (target, name: string, value) => { target[name] = value; return true; },
  });
  return { brush: brush as never, calls };
}

describe("painter recipes", () => {
  const colours = { colour: "#808080", accent: "#ff0000" };

  it("fills, then repeats a rect along and down with each row shifted", () => {
    const { brush, calls } = recorder();
    const steps: PaintStep[] = [
      { op: "fill", colour: "main" },
      { op: "rect", colour: "accent", x: 0, y: 0, width: 0.5, height: 0.2, repeat: { dx: 1, dy: 1, shift: 0.5 } },
    ];

    paintSteps(brush, steps, 2, 2, 10, colours);

    expect(calls[0]).toBe("fillRect:#808080:0,0,20,20");
    expect(calls.slice(1)).toEqual(["fillRect:#ff0000:0,0,5,2", "fillRect:#ff0000:10,0,5,2", "fillRect:#ff0000:5,10,5,2", "fillRect:#ff0000:15,10,5,2"]);
  });

  it("alternates a checker's two colours and lightens a shaded one", () => {
    const { brush, calls } = recorder();

    paintSteps(brush, [{ op: "checker", colour: "main", accent: "#000000", cell: 1 }], 2, 1, 10, colours);
    paintSteps(brush, [{ op: "fill", colour: "#000000", shade: 1 }], 1, 1, 1, colours);

    expect(calls).toEqual(["fillRect:#808080:0,0,10,10", "fillRect:#000000:10,0,10,10", "fillRect:#ffffff:0,0,1,1"]);
  });

  it("strokes an outline instead of filling, and never draws past the cap", () => {
    const { brush, calls } = recorder();

    paintSteps(brush, [{ op: "rect", colour: "main", width: 1, height: 1, outline: true }], 5, 5, 10, colours);
    const flood = recorder();
    paintSteps(flood.brush, [{ op: "rect", colour: "main", width: 0.1, height: 0.1, repeat: { dx: 0.1, dy: 0.1 } }], 100, 100, 1, colours);

    expect(calls).toEqual(["strokeRect:#808080:0,0,10,10"]);
    expect(flood.calls).toHaveLength(mostRects);
  });
});

describe("recipes in a look", () => {
  const room = (floor: string, wildlife?: string): LooksData["rooms"][number] => ({
    id: "loft",
    look: { sky: "#111111", floorColour: "#222222", wallColour: "#333333", floor: { painter: floor, colour: "#808080" }, wildlife, backdrop: { parts: [] } },
  });

  it("paints a floor with a recipe where no painter of that name is built in", () => {
    const { brush, calls } = recorder();
    setLooks({ rooms: [room("stripes", "moth")], variants: [], painters: [{ id: "stripes", surface: "floor", steps: [{ op: "fill", colour: "main" }] }], wildlife: [] });

    lookOf("loft").paintFloor?.(brush, { width: 2, depth: 2, pond: null, doors: [] } as never, 10);

    expect(calls).toEqual(["fillRect:#808080:0,0,20,20"]);
    expect(lookOf("loft").wildlife).toBe("moth");
  });

  it("leaves a floor unpainted when its recipe is gone, and a built-in painter wins over a recipe", () => {
    setLooks({ rooms: [room("stripes")], variants: [] });
    expect(lookOf("loft").paintFloor).toBeUndefined();

    setRecipes([], []);
  });

  it("hands the admin screen the body a recipe is saved with, and starters that read back", () => {
    expect(recipeBody("painters", { id: "a", surface: "wall", steps: [1] })).toEqual({ surface: "wall", steps: [1] });
    expect(recipeBody("wildlife", { id: "a", parts: [2], motion: { kind: "orbit" } })).toEqual({ parts: [2], motion: { kind: "orbit" } });
    expect(JSON.parse(JSON.stringify(newPainterTemplate()))).toMatchObject({ surface: "floor" });
    expect(JSON.parse(JSON.stringify(newWildlifeTemplate()))).toMatchObject({ motion: { kind: "wander" } });
  });
});

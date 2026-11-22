import { describe, expect, it } from "vitest";
import drafts from "../../src/Pspsps.Application/Rooms/ContentDrafts.json?raw";
import { mostRects, paintSteps, type PaintStep } from "./recipes";

type Draft = { painters: { id: string; surface: string; steps: PaintStep[] }[]; propDesigns: { kind: string; size: number; parts: { at: number[] }[] }[] };
const file = JSON.parse(drafts) as Draft;
const shore = file.painters.filter((p) => p.id.startsWith("shore-"));

describe("the shore rooms' floors", () => {
  it("are four painters, each a floor recipe of at most 64 steps", () => {
    expect(shore.map((p) => p.id).sort()).toEqual(["shore-beach", "shore-dock", "shore-peaks", "shore-village"]);
    for (const painter of shore) {
      expect(painter.surface).toBe("floor");
      expect(painter.steps.length).toBeLessThanOrEqual(64);
    }
  });

  it.each(shore.map((p) => [p.id, p] as const))("%s draws within the rect cap and every rect lies on the 28 by 16 floor", (_id, painter) => {
    let rects = 0;
    const brush = new Proxy({ fillStyle: "", strokeStyle: "" } as Record<string, unknown>, {
      get: (target, name: string) => (name in target ? target[name] : () => { rects++; }),
      set: (target, name: string, value) => { target[name] = value; return true; },
    });

    paintSteps(brush as never, painter.steps, 28, 16, 64, { colour: "#808080", accent: "#ff0000" });

    expect(rects).toBeGreaterThan(5);
    expect(rects).toBeLessThan(mostRects);
    for (const step of painter.steps.filter((s) => s.op === "rect")) {
      expect(step.x ?? 0).toBeGreaterThanOrEqual(-1e-9);
      expect((step.x ?? 0) + (step.width ?? 0)).toBeLessThanOrEqual(28 + 1e-9);
      expect(step.y ?? 0).toBeGreaterThanOrEqual(-1e-9);
      expect((step.y ?? 0) + (step.height ?? 0)).toBeLessThanOrEqual(16 + 1e-9);
    }
  });
});

describe("the shore prop designs", () => {
  const designs = file.propDesigns.filter((d) => d.kind.startsWith("shore-"));

  it("are 17, each with parts", () => {
    expect(designs).toHaveLength(17);
    for (const design of designs) {
      expect(design.parts.length).toBeGreaterThan(2);
      expect(design.size).toBeGreaterThan(0);
    }
  });
});

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

    expect(rects).toBeGreaterThanOrEqual(5);
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

describe("the paths between the shore rooms and their neighbours", () => {
  type Layout = { width: number; depth: number; place: { x: number; z: number }; paths: { edge: string; offset: number; width: number; length: number }[] };
  const world = JSON.parse(drafts) as unknown as { rooms: { id: string; layout: Layout; look: { floor?: { painter?: string } } }[]; painters: { id: string; steps: PaintStep[] }[] };
  const room = (id: string) => world.rooms.find((r) => r.id === id)!;
  const rectsOf = (id: string) => world.painters.find((p) => p.id === room(id).look.floor?.painter)!.steps.filter((s) => s.op === "rect");
  const water = /^#(d7f1f7|4fb3d9)$/;

  // World line (z for an east or west edge, x for a north or south one) that painted rects cross the edge on.
  const crossings = (id: string, edge: "East" | "West" | "North" | "South"): { from: number; to: number }[] => {
    const { width, depth, place } = room(id).layout;
    const x0 = place.x * 2;
    const z0 = place.z * 2;
    return rectsOf(id)
      .filter((s) => !water.test(s.colour ?? "") && (s.colour ?? "").startsWith("#"))
      .filter((s) => {
        const [x, y, w, h] = [s.x ?? 0, s.y ?? 0, s.width ?? 0, s.height ?? 0];
        return edge === "East" ? Math.abs(x + w - width) < 1e-6 && w < width : edge === "West" ? Math.abs(x) < 1e-6 && w < width
          : edge === "North" ? Math.abs(y) < 1e-6 && h < depth : Math.abs(y + h - depth) < 1e-6 && h < depth;
      })
      .map((s) => {
        const [x, y, w, h] = [s.x ?? 0, s.y ?? 0, s.width ?? 0, s.height ?? 0];
        return edge === "East" || edge === "West" ? { from: z0 + depth - (y + h), to: z0 + depth - y } : { from: x0 + x, to: x0 + x + w };
      });
  };
  const meets = (id: string, edge: "East" | "West" | "North" | "South", centre: number) =>
    crossings(id, edge).some((c) => c.to - c.from < 2 && Math.abs((c.from + c.to) / 2 - centre) < 0.05);

  it.each([
    ["beach", "East", "dock", "West"],
    ["dock", "East", "plaza", "West"],
    ["dock", "North", "peakside-village", "South"],
  ] as const)("%s's %s path meets a painted path on %s's %s edge, in the middle of the strip", (id, edge, other, otherEdge) => {
    const { width, depth, place, paths } = room(id).layout;
    const path = paths.find((p) => p.edge === edge)!;
    const centre = edge === "East" ? place.z * 2 + depth / 2 + path.offset : place.x * 2 + width / 2 + path.offset;

    expect(meets(id, edge, centre), `${id} ${edge}`).toBe(true);
    expect(meets(other, otherEdge, centre), `${other} ${otherEdge}`).toBe(true);
  });
});

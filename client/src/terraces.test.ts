import { describe, expect, it } from "vitest";
import { groundHeight, slabs, stepsReach } from "./terraces";

const terrace = { x: 10, z: 20, width: 8, depth: 6, height: 0.6 };

describe("slabs", () => {
  it("climbs in steps of about 0.2 m, the lowest the widest, the top exactly the terrace", () => {
    const steps = slabs(terrace);

    expect(steps.map((s) => s.top)).toEqual([0.2, 0.4, 0.6].map((v) => expect.closeTo(v, 6)));
    expect(steps[2]).toMatchObject({ width: 8, depth: 6 });
    expect(steps[0]!.width).toBeGreaterThan(steps[1]!.width);
    expect(steps[0]!.width).toBeLessThanOrEqual(8 + 2 * stepsReach);
  });

  it("is one step for a low terrace", () => {
    expect(slabs({ ...terrace, height: 0.1 })).toHaveLength(1);
  });
});

describe("groundHeight", () => {
  it("is the top on the terrace, lower on its steps, and zero past them", () => {
    expect(groundHeight([terrace], { x: 10, z: 20 })).toBeCloseTo(0.6);
    expect(groundHeight([terrace], { x: 14.2, z: 20 })).toBeCloseTo(0.4);
    expect(groundHeight([terrace], { x: 14.5, z: 20 })).toBeCloseTo(0.2);
    expect(groundHeight([terrace], { x: 15, z: 20 })).toBe(0);
  });

  it("is the highest of terraces that overlap, and zero with none", () => {
    expect(groundHeight([terrace, { ...terrace, height: 1 }], { x: 10, z: 20 })).toBeCloseTo(1);
    expect(groundHeight([], { x: 0, z: 0 })).toBe(0);
  });
});

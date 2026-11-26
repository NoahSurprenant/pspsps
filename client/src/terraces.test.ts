import { describe, expect, it } from "vitest";
import { groundHeight, slabs, stepsReach } from "./terraces";

const terrace = { x: 10, z: 20, width: 8, depth: 6, height: 0.6 };

describe("slabs", () => {
  it("climbs in steps of about 0.2 m, the lowest the widest, the top exactly the terrace", () => {
    const steps = slabs(terrace);

    expect(steps.map((s) => s.top)).toEqual([0.2, 0.4, 0.6].map((v) => expect.closeTo(v, 6)));
    expect(steps[2]).toMatchObject({ width: 8, depth: 6 });
    const widths = steps.map((step) => step.width);
    expect(widths).toEqual([...widths].sort((a, b) => b - a));
    expect(Math.max(...widths)).toBeGreaterThan(8);
    expect(Math.max(...widths)).toBeLessThanOrEqual(8 + 2 * stepsReach);
  });

  it("is one step for a low terrace", () => {
    expect(slabs({ ...terrace, height: 0.1 })).toHaveLength(1);
  });
});

describe("a round terrace", () => {
  const oval = { ...terrace, round: true };

  it("hands its shape to every slab", () => {
    expect(slabs(oval).every((slab) => slab.round === true)).toBe(true);
    expect(slabs(terrace).every((slab) => slab.round === undefined)).toBe(true);
  });

  it("covers the oval, not the corners of its rectangle", () => {
    expect(groundHeight([oval], { x: 10, z: 20 })).toBeCloseTo(0.6);
    expect(groundHeight([oval], { x: 13.5, z: 20 })).toBeCloseTo(0.6);
    expect(groundHeight([oval], { x: 13.9, z: 22.9 })).toBe(0);
    expect(groundHeight([terrace], { x: 13.9, z: 22.9 })).toBeCloseTo(0.6);
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

  it("hands a terrace's colour to every one of its slabs, and none when it has none", () => {
    expect(slabs({ ...terrace, colour: "#c4262e" }).map((slab) => slab.colour)).toEqual(["#c4262e", "#c4262e", "#c4262e"]);
    expect(slabs(terrace).every((slab) => slab.colour === undefined)).toBe(true);
  });
});

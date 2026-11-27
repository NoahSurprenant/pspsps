import { describe, expect, it } from "vitest";
import { groundHeight, rampHeight, rampMesh, slabs, stepsReach } from "./terraces";

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

  it("lifts a painted terrace's slabs a hair above stone of the same height, and no other", () => {
    expect(slabs({ ...terrace, colour: "#c4262e" }).map((slab) => slab.top)).toEqual(slabs(terrace).map((slab) => expect.closeTo(slab.top + 0.01, 6)));
  });

  it("hands a terrace's colour to every one of its slabs, and none when it has none", () => {
    expect(slabs({ ...terrace, colour: "#c4262e" }).map((slab) => slab.colour)).toEqual(["#c4262e", "#c4262e", "#c4262e"]);
    expect(slabs(terrace).every((slab) => slab.colour === undefined)).toBe(true);
  });
});

describe("a ramp", () => {
  const ramp = { x: -10, z: 0, width: 8, depth: 6, height: 2, slope: "west" as const };

  it("has no steps", () => {
    expect(slabs(ramp)).toEqual([]);
  });

  it("climbs evenly from the ground at its far side to its height at the side it rises toward", () => {
    expect(rampHeight(ramp, { x: -14, z: 0 })).toBeCloseTo(2, 6);
    expect(rampHeight(ramp, { x: -10, z: 2 })).toBeCloseTo(1, 6);
    expect(rampHeight(ramp, { x: -6, z: -3 })).toBeCloseTo(0, 6);
    expect(rampHeight(ramp, { x: -4, z: 0 })).toBe(0);
    expect(rampHeight(ramp, { x: -10, z: 3.5 })).toBe(0);
  });

  it.each([
    ["east", { x: -6, z: 0 }, { x: -14, z: 0 }],
    ["north", { x: -10, z: 3 }, { x: -10, z: -3 }],
    ["south", { x: -10, z: -3 }, { x: -10, z: 3 }],
  ] as const)("rises toward the %s", (slope, high, low) => {
    expect(rampHeight({ ...ramp, slope }, high)).toBeCloseTo(2, 6);
    expect(rampHeight({ ...ramp, slope }, low)).toBeCloseTo(0, 6);
  });

  it("sets the height a cat stands at, beside a stepped terrace", () => {
    expect(groundHeight([ramp, { x: 0, z: 0, width: 4, depth: 4, height: 0.4 }], { x: -12, z: 0 })).toBeCloseTo(1.5, 6);
    expect(groundHeight([ramp, { x: 0, z: 0, width: 4, depth: 4, height: 0.4 }], { x: 0, z: 0 })).toBeCloseTo(0.4, 6);
  });

  it("is a top and four sides, the top meeting the ground on its low side", () => {
    const { positions, topIndices, sideIndices } = rampMesh(ramp);
    const top = [...new Set(topIndices)].map((i) => positions.slice(i * 3, i * 3 + 3));

    expect(topIndices).toHaveLength(6);
    expect(sideIndices).toHaveLength(24);
    expect(top.map(([x, y]) => [x, y])).toEqual(expect.arrayContaining([[-14, 2], [-6, 0]]));
  });
});

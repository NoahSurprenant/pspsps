import { describe, expect, it } from "vitest";
import { fadeAt, groundBlendOf } from "./scene";

describe("fadeAt", () => {
  it("shows all of a room's ground over the room and none at the patch's edge, fading between", () => {
    // A patch 40 m wider each side than the room: the room is the middle, and the fade is 0.25 of the patch each way.
    expect(fadeAt(0.5, 0.5, 0.25, 0.25)).toBe(255);
    expect(fadeAt(0.25, 0.5, 0.25, 0.25)).toBe(255);
    expect(fadeAt(0, 0.5, 0.25, 0.25)).toBe(0);
    expect(fadeAt(0.5, 1, 0.25, 0.25)).toBe(0);
    expect(fadeAt(0.125, 0.5, 0.25, 0.25)).toBe(128);
  });

  it("fades by the nearer edge across the two directions", () => {
    expect(fadeAt(0.5, 0.0625, 0.25, 0.25)).toBe(64);
    expect(fadeAt(0.0625, 0.0625, 0.25, 0.25)).toBe(64);
  });
});

describe("groundBlendOf", () => {
  it("blends a room's ground into its neighbour's over a short way, but the ground under the whole world out over far more, so it ends at the horizon, not as a visible flat edge", () => {
    expect(groundBlendOf(false)).toBe(40);
    expect(groundBlendOf(true)).toBeGreaterThanOrEqual(200);
  });
});

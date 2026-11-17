import { describe, expect, it } from "vitest";
import { cameraPitch, cameraRadius, fieldOfView } from "./view";

/** The view's width at the floor's front edge, which is nearer the camera than the room's middle, so seen widest. */
const frontEdgeWidth = (radius: number, aspect: number, roomDepth: number) =>
  2 * (radius - (roomDepth / 2) * Math.sin(cameraPitch)) * Math.tan(fieldOfView / 2) * aspect;

describe("cameraRadius", () => {
  it("stands where it always has on a wide screen", () => {
    expect(cameraRadius(16 / 9, 24, 16)).toBeCloseTo(24 * 1.15);
  });

  it("stands back on a portrait phone until the floor's whole front edge shows", () => {
    const aspect = 390 / 844;
    const radius = cameraRadius(aspect, 24, 16);

    expect(radius).toBeGreaterThan(24 * 1.15);
    expect(frontEdgeWidth(radius, aspect, 16)).toBeGreaterThanOrEqual(24);
  });

  it("fits a small room on a small phone too", () => {
    const aspect = 360 / 640;

    expect(frontEdgeWidth(cameraRadius(aspect, 14, 10), aspect, 10)).toBeGreaterThanOrEqual(14);
  });
});

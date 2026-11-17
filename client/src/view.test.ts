import { describe, expect, it } from "vitest";
import { cameraRadius, fieldOfView } from "./view";

const visibleWidth = (radius: number, aspect: number) => 2 * radius * Math.tan(fieldOfView / 2) * aspect;

describe("cameraRadius", () => {
  it("stands where it always has on a wide screen", () => {
    expect(cameraRadius(16 / 9, 24, 16)).toBeCloseTo(24 * 1.15);
  });

  it("stands back on a portrait phone until the room's whole width shows", () => {
    const aspect = 390 / 844;
    const radius = cameraRadius(aspect, 24, 16);

    expect(radius).toBeGreaterThan(24 * 1.15);
    expect(visibleWidth(radius, aspect)).toBeGreaterThanOrEqual(24);
  });

  it("fits a small room on a small phone too", () => {
    const aspect = 360 / 640;

    expect(visibleWidth(cameraRadius(aspect, 14, 10), aspect)).toBeGreaterThanOrEqual(14);
  });
});

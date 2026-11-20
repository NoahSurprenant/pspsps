import { describe, expect, it } from "vitest";
import { cameraFocus, cameraPitch, cameraRadius, fieldOfView, roomPitch, startingZoom, zoomRadius } from "./view";

/** The view's width at the floor's front edge, which is nearer the camera than the room's middle, so seen widest. */
const frontEdgeWidth = (radius: number, aspect: number, roomDepth: number) =>
  2 * (radius - (roomDepth / 2) * Math.sin(roomPitch(aspect))) * Math.tan(fieldOfView / 2) * aspect;

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

  it("shows the floor from higher up in portrait and keeps its angle on wide screens", () => {
    expect(roomPitch(390 / 844)).toBeLessThan(cameraPitch);
    expect(roomPitch(844 / 390)).toBe(cameraPitch);
  });
});

describe("zooming the room", () => {
  const room = { width: 24, depth: 16 };
  const portrait = 390 / 844;

  it("starts closer on portrait touch screens and fits landscape or desktop", () => {
    expect(startingZoom(portrait, true)).toBeGreaterThan(1);
    expect(startingZoom(portrait, false)).toBe(1);
    expect(startingZoom(844 / 390, true)).toBe(1);
  });

  it("limits zoom both ways and keeps the whole-room view centred", () => {
    expect(zoomRadius(30, 0.5)).toBe(30);
    expect(zoomRadius(30, 100)).toBe(9);
    expect(cameraFocus(room, { x: 8, z: 5 }, portrait, 30, 30)).toEqual({ x: 0, z: 0 });
  });

  it("follows the cat and stops panning before going beyond the room", () => {
    const fitted = cameraRadius(portrait, room.width, room.depth);
    expect(cameraFocus(room, { x: -2, z: 2 }, portrait, 12, fitted)).toEqual({ x: -2, z: 2 });
    const corner = cameraFocus(room, { x: 12, z: 8 }, portrait, 12, fitted);
    expect(corner.x).toBeGreaterThan(2);
    expect(corner.x).toBeLessThan(12);
    expect(corner.z).toBeGreaterThan(2);
    expect(corner.z).toBeLessThan(8);
  });
});

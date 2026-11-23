import { describe, expect, it } from "vitest";
import { VIEW } from "./islandMap";
import { WHOLE, ZOOM, cameraEdges, clampFrame, panBy, project, zoomAt } from "./mapZoom";

describe("zooming the island map", () => {
  it("shows the whole picture as it was, and the camera sees all of it", () => {
    expect(project(20, 80, WHOLE)).toEqual({ left: 20, top: 80 });
    const edges = cameraEdges(WHOLE);
    expect(edges).toEqual({ left: -VIEW.halfWidth, right: VIEW.halfWidth, top: VIEW.halfHeight, bottom: -VIEW.halfHeight });
  });

  it("keeps the spot under the finger where it is while zooming, and never goes past the limits", () => {
    const before = project(70, 30, zoomAt(WHOLE, 2, 70, 30));
    expect(before.left).toBeCloseTo(70);
    expect(before.top).toBeCloseTo(30);
    expect(zoomAt(WHOLE, 100).zoom).toBe(ZOOM.max);
    expect(zoomAt(WHOLE, 0.01).zoom).toBe(ZOOM.min);
  });

  it("zooming out all the way shows the whole picture again, wherever it was dragged", () => {
    const dragged = panBy(zoomAt(WHOLE, 2), -40, 10);
    expect(zoomAt(dragged, 0.01)).toEqual(WHOLE);
  });

  it("cannot be dragged off the picture", () => {
    const frame = clampFrame({ zoom: 2, cx: 0, cy: 100 });
    expect(frame).toEqual({ zoom: 2, cx: 25, cy: 75 });
    expect(panBy(frame, 500, -500)).toEqual(frame);
  });

  it("drags the picture the way the finger moves", () => {
    const zoomed = zoomAt(WHOLE, 2);
    expect(panBy(zoomed, 10, 0).cx).toBeLessThan(zoomed.cx);
  });

  it("points the camera at the part showing", () => {
    const edges = cameraEdges({ zoom: 2, cx: 75, cy: 50 });
    expect(edges.left).toBeCloseTo(0);
    expect(edges.right).toBeCloseTo(VIEW.halfWidth);
    expect(edges.top).toBeCloseTo(VIEW.halfHeight / 2);
  });
});

import { VIEW } from "./islandMap";

/** What part of the picture is showing: how far in, and the centre as a percent of the whole picture. */
export interface Frame {
  zoom: number;
  cx: number;
  cy: number;
}

export const ZOOM = { min: 1, max: 3, step: 1.5 };
export const WHOLE: Frame = { zoom: 1, cx: 50, cy: 50 };

const between = (value: number, low: number, high: number) => Math.min(high, Math.max(low, value));

/** Keeps the zoom in range and the view inside the picture. */
export function clampFrame({ zoom, cx, cy }: Frame): Frame {
  const z = between(zoom, ZOOM.min, ZOOM.max);
  const half = 50 / z;
  return { zoom: z, cx: between(cx, half, 100 - half), cy: between(cy, half, 100 - half) };
}

/** Where a spot of the whole picture (percent) lands on what is showing (percent). */
export function project(left: number, top: number, frame: Frame): { left: number; top: number } {
  return { left: 50 + (left - frame.cx) * frame.zoom, top: 50 + (top - frame.cy) * frame.zoom };
}

/** Zooms by a factor while the spot under (pointX, pointY), percent of what is showing, stays put. */
export function zoomAt(frame: Frame, factor: number, pointX = 50, pointY = 50): Frame {
  const zoom = between(frame.zoom * factor, ZOOM.min, ZOOM.max);
  const x = frame.cx + (pointX - 50) / frame.zoom;
  const y = frame.cy + (pointY - 50) / frame.zoom;
  return clampFrame({ zoom, cx: x - (pointX - 50) / zoom, cy: y - (pointY - 50) / zoom });
}

/** Drags the picture by an amount of what is showing (percent). */
export const panBy = (frame: Frame, dx: number, dy: number): Frame =>
  clampFrame({ ...frame, cx: frame.cx - dx / frame.zoom, cy: frame.cy - dy / frame.zoom });

/** The camera's edges for a frame, in the units of VIEW. */
export function cameraEdges(frame: Frame): { left: number; right: number; top: number; bottom: number } {
  const half = 50 / frame.zoom;
  const across = (percent: number) => ((percent - 50) / 50) * VIEW.halfWidth;
  const up = (percent: number) => ((50 - percent) / 50) * VIEW.halfHeight;
  return { left: across(frame.cx - half), right: across(frame.cx + half), top: up(frame.cy - half), bottom: up(frame.cy + half) };
}

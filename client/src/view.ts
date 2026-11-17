/** The camera's vertical field of view, in radians. */
export const fieldOfView = 0.7;

/**
 * How far the room's camera stands back. Far enough for the room as on a wide screen; on a narrow (portrait) screen,
 * where the same view would cut the room's sides off, far enough that its whole width fits, with a little margin.
 */
export function cameraRadius(aspect: number, roomWidth: number, roomDepth: number): number {
  const wide = Math.max(roomWidth, roomDepth) * 1.15;
  // The view's width at distance r is 2 r tan(fov / 2) * aspect.
  const fitWidth = (roomWidth * 1.1) / (2 * Math.tan(fieldOfView / 2) * aspect);
  return Math.max(wide, fitWidth);
}

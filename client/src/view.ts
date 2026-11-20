/** The camera's vertical field of view, in radians. */
export const fieldOfView = 0.7;

/** How far the camera tips from straight down, in radians. */
export const cameraPitch = 0.95;

/**
 * How far the room's camera stands back. Far enough for the room as on a wide screen; on a narrow (portrait) screen,
 * where the same view would cut the room's sides off, far enough that its whole width fits, with a little margin, at
 * the floor's front edge: nearer the camera than the room's middle, it is the widest part of the view.
 */
export function cameraRadius(aspect: number, roomWidth: number, roomDepth: number): number {
  const wide = Math.max(roomWidth, roomDepth) * 1.15;
  // The view's width at distance r is 2 r tan(fov / 2) * aspect; the front edge is (depth / 2) sin(pitch) nearer.
  const fitWidth = (roomWidth * 1.06) / (2 * Math.tan(fieldOfView / 2) * aspect)
    + (roomDepth / 2) * Math.sin(roomPitch(aspect));
  return Math.max(wide, fitWidth);
}

/** A more overhead view on narrow screens gives the playable floor more of the tall canvas. */
export function roomPitch(aspect: number): number {
  return cameraPitch - Math.min(0.22, Math.max(0, 0.8 - aspect));
}

/** Portrait touch screens start closer. The whole room is still one pinch away. */
export function startingZoom(aspect: number, touch: boolean): number {
  return touch && aspect < 0.8 ? 1.4 : 1;
}

/** Keep the floor in front of the camera at the closest view, even in a deep room. */
export function zoomRadius(fitted: number, zoom: number): number {
  return Math.min(fitted, Math.max(9, fitted / zoom));
}

/** Aim at the cat until moving the camera farther would show more space outside the room. */
export function cameraFocus(
  room: { width: number; depth: number },
  cat: { x: number; z: number } | undefined,
  aspect: number,
  radius: number,
  fitted: number,
): { x: number; z: number } {
  if (!cat || radius >= fitted) {
    return { x: 0, z: 0 };
  }
  const halfView = radius * Math.tan(fieldOfView / 2);
  const limitX = Math.max(0, room.width / 2 - halfView * aspect);
  const limitZ = Math.max(0, room.depth / 2 - halfView / Math.cos(roomPitch(aspect)));
  return { x: Math.max(-limitX, Math.min(cat.x, limitX)), z: Math.max(-limitZ, Math.min(cat.z, limitZ)) };
}

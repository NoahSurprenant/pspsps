interface Spot {
  x: number;
  z: number;
}

interface RoomSize {
  width: number;
  depth: number;
}

interface RayPoint extends Spot {
  y: number;
}

function limit(value: number, half: number): number {
  return Math.min(half, Math.max(-half, value));
}

/** Keep the walk aimed at the click, stopping where the line from the cat first leaves the floor. */
export function walkToEdge(from: Spot, target: Spot, room: RoomSize): Spot {
  const dx = target.x - from.x;
  const dz = target.z - from.z;
  const halfWidth = room.width / 2;
  const halfDepth = room.depth / 2;
  const across = dx === 0 ? Infinity : (Math.sign(dx) * halfWidth - from.x) / dx;
  const ahead = dz === 0 ? Infinity : (Math.sign(dz) * halfDepth - from.z) / dz;
  const fraction = Math.min(1, across, ahead);
  return {
    x: limit(from.x + fraction * dx, halfWidth),
    z: limit(from.z + fraction * dz, halfDepth),
  };
}

/** Project a click past the floor onto it. A click at or above the horizon aims towards the back wall. */
export function sceneryTarget(from: Spot, ray: { origin: RayPoint; direction: RayPoint }, room: RoomSize): Spot {
  const { origin, direction } = ray;
  const distance = direction.y < -0.0001
    ? -origin.y / direction.y
    : (room.depth / 2 - origin.z) / direction.z;
  return walkToEdge(from, { x: origin.x + direction.x * distance, z: origin.z + direction.z * distance }, room);
}

/** Keyboard steps can slide along an edge while the player holds two directions. */
export function stepInRoom(from: Spot, dx: number, dz: number, room: RoomSize): Spot {
  const stride = 0.7 / Math.hypot(dx, dz);
  return {
    x: limit(from.x + dx * stride, room.width / 2),
    z: limit(from.z + dz * stride, room.depth / 2),
  };
}

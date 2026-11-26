export interface Flat {
  x: number;
  z: number;
}

/** How quickly a cat on ice gets its speed up or down, per second: a grip of 1 would take about a second to turn. */
export const iceGrip = 1.5;

/** Slows a walk to a stop over the last stretch to its spot, per metre of distance. */
const brake = 2.2;

/** What a cat's walking speed (metres a second) becomes on ice in one frame: it speeds up, turns and stops slowly, so it slides. */
export function slide(velocity: Flat, from: Flat, to: Flat, speed: number, seconds: number): Flat {
  const dx = to.x - from.x;
  const dz = to.z - from.z;
  const distance = Math.hypot(dx, dz);
  const pace = distance < 0.02 ? 0 : Math.min(speed, distance * brake);
  const wanted = distance < 0.02 ? { x: 0, z: 0 } : { x: (dx / distance) * pace, z: (dz / distance) * pace };
  const blend = Math.min(1, iceGrip * seconds);
  const x = velocity.x + (wanted.x - velocity.x) * blend;
  const z = velocity.z + (wanted.z - velocity.z) * blend;
  return Math.hypot(x, z) < 0.05 && pace === 0 ? { x: 0, z: 0 } : { x, z };
}

/** Whether a spot is on a room's floor (its middle and size, in world metres). */
export function onFloor(spot: Flat, room: { x: number; z: number; width: number; depth: number }): boolean {
  return Math.abs(spot.x - room.x) <= room.width / 2 && Math.abs(spot.z - room.z) <= room.depth / 2;
}

export interface Ball {
  kind: string;
  x: number;
  z: number;
  velocityX: number;
  velocityZ: number;
}

/** Where to draw a ball that was last told to be at (x, z) going (velocityX, velocityZ), <seconds> ago, easing from where it is drawn. */
export function followBall(drawn: Flat, told: Ball, seconds: number, frame: number): Flat {
  const ahead = { x: told.x + told.velocityX * seconds, z: told.z + told.velocityZ * seconds };
  const ease = Math.min(1, frame * 14);
  return { x: drawn.x + (ahead.x - drawn.x) * ease, z: drawn.z + (ahead.z - drawn.z) * ease };
}

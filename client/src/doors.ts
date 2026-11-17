import type { DoorView } from "./types";

/** The door a spot on the floor is on, if any: walking there goes through it. */
export function doorAt(doors: readonly DoorView[], x: number, z: number): DoorView | undefined {
  return doors.find((door) => Math.hypot(x - door.x, z - door.z) <= door.radius);
}

/**
 * The player's walk to a door, which goes through it once the cat is drawn standing on it. Only the latest walk goes
 * through: any other walk (a floor click, fishing, another room) started after it calls it off.
 */
export class DoorWalks {
  private walks = 0;
  private going: number | undefined;

  /** A walk to a door starts: its number, to check it with. */
  start(): number {
    this.walks += 1;
    this.going = this.walks;
    return this.walks;
  }

  /** Whatever the cat does next instead: the walk to a door, if one is going, won't go through. */
  callOff(): void {
    this.going = undefined;
  }

  /** Whether <walk> is still the walk going, and so should go through its door. */
  isGoing(walk: number): boolean {
    return this.going === walk;
  }
}

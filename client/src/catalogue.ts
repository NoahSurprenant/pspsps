// The shop's catalogue stand (src/Pspsps.Domain/Rooms/Prop.cs, PropKinds.CatalogueStand): the one prop that does
// something. Tapped, the cat walks up to it and the catalogue opens once it is there; a cat near it already has it open
// at once. The server sells only in the room it stands in.
import type { PropView } from "./types";

/** The kind of prop the catalogue stand is. */
export const catalogueStandKind = "catalogue-stand";

/** How far past the stand's edge a cat reading it stands: its middle, half a cat clear. */
const readingGap = 0.6;

/** How far past the stand's edge a cat can still read it, without walking up. */
const reach = 1.3;

/** The room's catalogue stand, if it has one (the shop). */
export function catalogueStand(props: readonly PropView[] | null | undefined): PropView | undefined {
  return props?.find((prop) => prop.kind === catalogueStandKind);
}

/**
 * Where a cat stands to read the catalogue: beside the stand, on its right as seen from its front (a stand facing 0
 * faces the front of the room, -Z, where the camera is: its right is +X), clear of it. Not in front of it, where the
 * cat would stand between the camera and the stand and take the next tap on it (the player's own cat comes first).
 */
export function readingSpot(stand: PropView): { x: number; z: number } {
  const away = stand.radius + readingGap;
  return { x: stand.x + Math.cos(stand.facing) * away, z: stand.z - Math.sin(stand.facing) * away };
}

/** Whether a cat drawn at (x, z) is near enough the stand to read it where it stands. */
export function canRead(stand: PropView, cat: { x: number; z: number }): boolean {
  return Math.hypot(cat.x - stand.x, cat.z - stand.z) <= stand.radius + reach;
}

/** What reading the catalogue needs: the cat's walks, and how to move it, see it there, and open the catalogue. */
export interface Reading {
  /** The player's walks to a door or the stand: only the latest gets where it goes and does it. */
  walks: { start(): number; callOff(): void; isGoing(walk: number): boolean };
  move(x: number, z: number): Promise<void>;
  /** Resolves true once the cat is drawn standing at (x, z), false if it goes elsewhere first. */
  reached(x: number, z: number): Promise<boolean>;
  open(): Promise<void>;
}

/**
 * The stand tapped, with the player's cat drawn at <cat> (where it is on screen, not where it is walking to): near enough, the catalogue opens at once; otherwise the cat walks up
 * to it and the catalogue opens once it is drawn there, unless it was sent somewhere else meanwhile.
 */
export async function goRead(stand: PropView, cat: { x: number; z: number }, how: Reading): Promise<void> {
  if (canRead(stand, cat)) {
    how.walks.callOff();
    await how.open();
    return;
  }
  const walk = how.walks.start();
  const spot = readingSpot(stand);
  await how.move(spot.x, spot.z);
  if (await how.reached(spot.x, spot.z) && how.walks.isGoing(walk)) {
    how.walks.callOff();
    await how.open();
  }
}

// The shop's catalogue stand (src/Pspsps.Domain/Rooms/Prop.cs, PropKinds.CatalogueStand): the one prop that does
// something. Tapped, the cat walks up to it and the catalogue opens once it is there; a cat near it already has it open
// at once. The server sells only in the room it stands in.
import type { PropView } from "./types";

/** The kind of prop the catalogue stand is. */
export const catalogueStandKind = "catalogue-stand";

/** How far past the stand's edge a cat reads it from. */
const readingGap = 0.45;

/** How far past the stand's edge a cat can still read it, without walking up. */
const reach = 1.3;

/** The room's catalogue stand, if it has one (the shop). */
export function catalogueStand(props: readonly PropView[] | null | undefined): PropView | undefined {
  return props?.find((prop) => prop.kind === catalogueStandKind);
}

/**
 * Where a cat stands to read the catalogue: just in front of the stand, the side it faces (a stand facing 0 faces the
 * front of the room, -Z, where the camera is), clear of it.
 */
export function readingSpot(stand: PropView): { x: number; z: number } {
  const away = stand.radius + readingGap;
  return { x: stand.x - Math.sin(stand.facing) * away, z: stand.z - Math.cos(stand.facing) * away };
}

/** Whether a cat at (x, z) is near enough the stand to read it where it stands. */
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
 * The stand tapped, with the player's cat at <cat>: near enough, the catalogue opens at once; otherwise the cat walks up
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

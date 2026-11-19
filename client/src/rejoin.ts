import type { RoomSnapshot } from "./types";

/** Where a tab was: a world and one of its rooms. */
export interface Whereabouts {
  worldId: string;
  roomId: string;
}

/**
 * Walks back in to where the tab was, after the server forgot it there (a dropped connection, or a room that lost our
 * cat): enter the world again, which lands in its first room, then go back to the room it was in if that's another.
 * `enter` resolves to the room it landed in, or undefined when entering failed.
 */
export async function walkBackIn(
  where: Whereabouts,
  enter: (worldId: string) => Promise<string | undefined>,
  goTo: (roomId: string) => Promise<unknown>,
): Promise<void> {
  const landedIn = await enter(where.worldId);
  if (landedIn !== undefined && landedIn !== where.roomId) {
    await goTo(where.roomId);
  }
}

/**
 * Comes back to where the tab was. After a lost room the tab is still in its world, so it only goes back to the room
 * (entering the world again could find it full, and would flash the cat through the world's first room); after a
 * reconnect the server has forgotten the tab entirely, so it walks back in through the world.
 */
export async function comeBack(
  reason: "roomLost" | "reconnected",
  where: Whereabouts,
  enter: (worldId: string) => Promise<string | undefined>,
  goTo: (roomId: string) => Promise<unknown>,
): Promise<void> {
  if (reason === "roomLost") {
    await goTo(where.roomId);
    return;
  }
  await walkBackIn(where, enter, goTo);
}

/** What a room that has just shown says: why the cat is there, when it isn't where it was going (the server's notice). */
export function landingNotice(shown: Pick<RoomSnapshot, "notice">): string | undefined {
  return shown.notice ?? undefined;
}

/**
 * Whether the tab says again why its cat was sent out of a home, once out: only when it went where the message said (Town
 * Square). A cat that landed somewhere else (Town Square full) has been told where and why instead; one that went home
 * needs no repeat; and a walk that failed has said why.
 */
export function saysSentOutAgain(shown: Pick<RoomSnapshot, "home" | "notice"> | undefined): boolean {
  return shown !== undefined && !shown.home && !shown.notice;
}

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

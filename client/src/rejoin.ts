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

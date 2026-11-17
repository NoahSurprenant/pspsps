/** The game's name: the page's title outside a room. */
export const gameName = "pspsps";

/**
 * The page's title: in a room, the room's name first ("Cat Café · pspsps"), so the tab and a screen reader still say
 * where the cat is (the room bar has no room for its name); elsewhere, the game's name.
 */
export function pageTitle(roomName?: string): string {
  return roomName ? `${roomName} · ${gameName}` : gameName;
}

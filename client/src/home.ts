import type { FurnitureView, HomeView, HomeVisitors, PropView, Shop, ShopItem } from "./types";

/** A piece of furniture as the room's decoration: its kind (the shop item), where, its circle, and which way it faces. */
export function propOf(piece: FurnitureView): PropView {
  return { kind: piece.itemId, x: piece.x, z: piece.z, radius: piece.radius, facing: (piece.quarterTurns * Math.PI) / 2 };
}

/** A home's furniture as the room's props. */
export function propsOf(home: HomeView): PropView[] {
  return home.furniture.map(propOf);
}

/** Of two views of a home, the newer: changes can arrive out of order (an answer, and the room's broadcast). */
export function newer(current: HomeView | undefined, next: HomeView): HomeView {
  return current && current.ownerId === next.ownerId && current.version > next.version ? current : next;
}

/** How many of <itemId> the player has, as the shop says (one of anything owned, from a server that sends no counts). */
export function countOf(shop: Shop, itemId: string): number {
  return shop.counts?.[itemId] ?? (shop.owned.includes(itemId) ? 1 : 0);
}

/** A piece of furniture the player has: how many, how many are out in their home, and how many are left to put out. */
export interface FurnitureLeft {
  item: ShopItem;
  owned: number;
  out: number;
  left: number;
}

/** The furniture the player has, in the shop's order, with how many of each are still to put out. */
export function furnitureLeft(shop: Shop, home: HomeView): FurnitureLeft[] {
  return shop.items
    .filter((item) => item.kind === "Furniture")
    .map((item) => {
      const owned = countOf(shop, item.id);
      const out = home.furniture.filter((piece) => piece.itemId === item.id).length;
      return { item, owned, out, left: Math.max(0, owned - out) };
    })
    .filter((row) => row.owned > 0);
}

/** The piece of furniture standing at (x, z) on the floor, if any: the nearest whose circle holds the spot. */
export function pieceAt(furniture: readonly FurnitureView[], x: number, z: number): FurnitureView | undefined {
  return furniture
    .map((piece) => ({ piece, distance: Math.hypot(x - piece.x, z - piece.z) }))
    .filter(({ piece, distance }) => distance <= piece.radius)
    .sort((a, b) => a.distance - b.distance)[0]?.piece;
}

/** Who may visit, as the owner picks it, in this order. */
export const visitorChoices: readonly { value: HomeVisitors; label: string }[] = [
  { value: "Friends", label: "Friends" },
  { value: "Anyone", label: "Anyone" },
  { value: "Nobody", label: "Nobody" },
];

/** What the owner is doing while decorating: a piece of their furniture picked to put out, or a piece out selected. */
export interface Decorating {
  /** The shop item picked to put out: a tap on the floor puts it there. */
  picked?: string;
  /** The piece out in the home that is selected: to turn or put away. */
  selected?: string;
}

/** What the decorating panel says to do next. */
export function decoratingHint(decorating: Decorating, left: readonly FurnitureLeft[], home: HomeView): string {
  const selected = home.furniture.find((piece) => piece.id === decorating.selected);
  if (selected) {
    return `The ${selected.name}: turn it, or put it away.`;
  }
  const picked = left.find((row) => row.item.id === decorating.picked);
  if (picked) {
    return `Tap the floor where the ${picked.item.name} goes, or let it find a spot.`;
  }
  if (left.length === 0) {
    return "You have no furniture yet. Buy some in the shop.";
  }
  return home.furniture.length > 0
    ? "Pick something to put out, or tap a piece in the room to turn it or put it away."
    : "Pick something to put out.";
}

/** Whether the player is in their own home. */
export function isOwnHome(home: HomeView | null | undefined, playerId: string | undefined): boolean {
  return !!home && home.ownerId === playerId;
}

/** What the owner is told once they've changed who may visit. */
export function visitorsMessage(visitors: HomeVisitors): string {
  switch (visitors) {
    case "Friends":
      return "Only your friends can visit now.";
    case "Anyone":
      return "Any cat can visit now.";
    case "Nobody":
      return "Nobody can visit now.";
  }
}

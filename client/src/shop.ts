import { countOf } from "./home";
import type { ItemKind, OutfitView, PlayerCard, Shop, ShopItem } from "./types";

/** What a shop button does (the hub's Buy, Wear and TakeOff). */
export type ShopAction = "buy" | "wear" | "takeOff";

export interface ShopButton {
  action: ShopAction;
  label: string;
  /** Its name for a screen reader: the label, and which item. */
  ariaLabel: string;
  primary: boolean;
  disabled: boolean;
}

/** One item in the shop as the player sees it: what it says next to its name, and its button (none for the fur worn). */
export interface ShopRow {
  item: ShopItem;
  status: string;
  button?: ShopButton;
  /** Why it can't be bought yet. */
  reason?: string;
}

/** How many of one piece of furniture a player can have (the server's ShopCatalog.MaxOfEachFurniture). */
export const maxOfEachFurniture = 5;

/** The shop's parts, in this order. */
const sectionTitles: Record<ItemKind, string> = { Hat: "Hats", Collar: "Collars", Fur: "Fur", Furniture: "Furniture" };

/** Whether the player's cat has <item> on: the hat, the collar, or the coat. Furniture is never worn. */
export function isWorn(shop: Shop, item: ShopItem): boolean {
  switch (item.kind) {
    case "Hat":
      return shop.hat === item.id;
    case "Collar":
      return shop.collar === item.id;
    case "Fur":
      return shop.furColor === item.furColor;
    case "Furniture":
      return false;
  }
}

/**
 * An item's row: worn, it can be taken off (not a fur: a cat always has one); the player's, put on; furniture, bought
 * again up to the most a cat can keep; anything else, bought for its price, with Buy disabled and the coins still
 * needed said when the player hasn't enough.
 */
export function shopRow(shop: Shop, item: ShopItem): ShopRow {
  if (item.kind === "Furniture") {
    return furnitureRow(shop, item);
  }
  if (isWorn(shop, item)) {
    return item.kind === "Fur"
      ? { item, status: "Your fur now" }
      : { item, status: "Wearing", button: { action: "takeOff", label: "Take off", ariaLabel: `Take off the ${item.name}`, primary: false, disabled: false } };
  }
  if (shop.owned.includes(item.id)) {
    return { item, status: "Yours", button: { action: "wear", label: "Wear", ariaLabel: `Wear the ${item.name}`, primary: true, disabled: false } };
  }
  return buyRow(shop, item, "Buy", `Buy the ${item.name} for ${item.price} coins`, `🪙 ${item.price}`);
}

/** A piece of furniture: how many the player has, and Buy (or Buy another, until they have as many as a cat keeps). */
function furnitureRow(shop: Shop, item: ShopItem): ShopRow {
  const owned = countOf(shop, item.id);
  if (owned >= maxOfEachFurniture) {
    return { item, status: `You have ${owned}, as many as a cat can keep` };
  }
  return owned > 0
    ? buyRow(shop, item, "Buy another", `Buy another ${item.name} for ${item.price} coins`, `🪙 ${item.price} · you have ${owned}`)
    : buyRow(shop, item, "Buy", `Buy the ${item.name} for ${item.price} coins`, `🪙 ${item.price}`);
}

/** Bought for its price, with Buy disabled and the coins still needed said when the player hasn't enough. */
function buyRow(shop: Shop, item: ShopItem, label: string, ariaLabel: string, status: string): ShopRow {
  const short = item.price - shop.coins;
  const button = { action: "buy" as const, label, ariaLabel, primary: short <= 0, disabled: short > 0 };
  return short > 0 ? { item, status, button, reason: `need ${short} more` } : { item, status, button };
}

/** Whether <shop> already shows the player's cat dressed as <outfit>: nothing to fetch again. */
export function showsOutfit(shop: Shop, outfit: OutfitView): boolean {
  return shop.furColor === outfit.furColor && shop.hat === outfit.hat && shop.collar === outfit.collar;
}

/** The shop in its parts (hats, collars, fur, furniture), each in the server's order. */
export function shopSections(shop: Shop): { kind: ItemKind; title: string; rows: ShopRow[] }[] {
  return (Object.keys(sectionTitles) as ItemKind[])
    .map((kind) => ({ kind, title: sectionTitles[kind], rows: shop.items.filter((i) => i.kind === kind).map((i) => shopRow(shop, i)) }))
    .filter((section) => section.rows.length > 0);
}

/** What a shop button did, said to the player. */
export function shopMessage(action: ShopAction, item: ShopItem | undefined): string {
  const name = item?.name ?? "it";
  switch (action) {
    case "buy":
      return item?.kind === "Furniture"
        ? `You bought the ${name}! Put it out at home: Friends, then Go home.`
        : `You bought the ${name}! Wear it whenever you like.`;
    case "wear":
      return `You're wearing the ${name}.`;
    case "takeOff":
      return `You took off the ${name}.`;
  }
}

/** What a player card says the cat wears; nothing when it wears neither a hat nor a collar. */
export function wearingLine(card: Pick<PlayerCard, "hatName" | "collarName">): string | undefined {
  const worn = [card.hatName, card.collarName].filter((name): name is string => !!name);
  return worn.length > 0 ? `Wearing: ${worn.join(", ")}` : undefined;
}

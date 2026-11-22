import { countOf } from "./home";
import type { ItemKind, OutfitView, PlayerCard, Shop, ShopItem } from "./types";

/** What a catalogue or wardrobe button does (the hub's Buy, Wear and TakeOff). */
export type ShopAction = "buy" | "wear" | "takeOff";

export interface ShopButton {
  action: ShopAction;
  label: string;
  /** Its name for a screen reader: the label, and which item. */
  ariaLabel: string;
  primary: boolean;
  disabled: boolean;
}

/** One item as the player sees it: what it says next to its name, and its button, if any. */
export interface ShopRow {
  item: ShopItem;
  status: string;
  button?: ShopButton;
  /** Why it can't be bought yet. */
  reason?: string;
}

/** A part of the catalogue or the wardrobe: one kind of item. */
export interface ShopSection {
  kind: ItemKind;
  title: string;
  rows: ShopRow[];
}

/** The parts, in this order. */
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
 * An item's row in the catalogue (the shop's, where things are bought): what the player has already says so (worn, or
 * theirs: put on from their own card); furniture is bought again up to the most a cat can keep; anything else is bought
 * for its price, with Buy disabled and the coins still needed said when the player hasn't enough.
 */
export function catalogueRow(shop: Shop, item: ShopItem): ShopRow {
  if (item.kind === "Furniture") {
    return furnitureRow(shop, item);
  }
  if (isWorn(shop, item)) {
    return { item, status: item.kind === "Fur" ? "Your fur now" : "Wearing" };
  }
  if (shop.owned.includes(item.id)) {
    return { item, status: "Yours" };
  }
  return buyRow(shop, item, "Buy", `Buy the ${item.name} for ${item.price} coins`, `🪙 ${item.price}`);
}

/**
 * An item's row in the player's own wardrobe (their card): worn, it can be taken off (not a fur: a cat always has one);
 * otherwise put on.
 */
export function wardrobeRow(shop: Shop, item: ShopItem): ShopRow {
  if (isWorn(shop, item)) {
    return item.kind === "Fur"
      ? { item, status: "Your fur now" }
      : { item, status: "Wearing", button: { action: "takeOff", label: "Take off", ariaLabel: `Take off the ${item.name}`, primary: false, disabled: false } };
  }
  return { item, status: "Yours", button: { action: "wear", label: "Wear", ariaLabel: `Wear the ${item.name}`, primary: true, disabled: false } };
}

/** A piece of furniture: how many the player has, and Buy (or Buy another, until they have as many as a cat keeps). */
function furnitureRow(shop: Shop, item: ShopItem): ShopRow {
  const owned = countOf(shop, item.id);
  if (owned >= shop.maxOfEachFurniture) {
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

/** The items of <shop> picked by <keep>, in their parts (hats, collars, fur, furniture), each in the server's order. */
function sectionsOf(shop: Shop, keep: (item: ShopItem) => boolean, row: (shop: Shop, item: ShopItem) => ShopRow): ShopSection[] {
  return (Object.keys(sectionTitles) as ItemKind[])
    .map((kind) => ({ kind, title: sectionTitles[kind], rows: shop.items.filter((i) => i.kind === kind && keep(i)).map((i) => row(shop, i)) }))
    .filter((section) => section.rows.length > 0);
}

/** The catalogue: everything in the shop, in its parts. */
export function catalogueSections(shop: Shop): ShopSection[] {
  return sectionsOf(shop, () => true, catalogueRow);
}

/** The player's wardrobe: the hats, collars and furs they have (furniture goes in their home, not on their cat). */
export function wardrobeSections(shop: Shop): ShopSection[] {
  return sectionsOf(shop, (item) => item.kind !== "Furniture" && shop.owned.includes(item.id), wardrobeRow);
}

/**
 * Whether the player has anything to wear besides the coat on their cat now: a hat or collar (worn, to take off, or not,
 * to put on), or another coat. With nothing, their card says where things are sold.
 */
export function hasMoreToWear(shop: Shop): boolean {
  return wardrobeSections(shop).some((section) => section.rows.some((row) => row.button));
}

/** What a button did, said to the player. */
export function shopMessage(action: ShopAction, item: ShopItem | undefined): string {
  const name = item?.name ?? "it";
  switch (action) {
    case "buy":
      return item?.kind === "Furniture"
        ? `You bought the ${name}! Put it out at home: Friends, then Go home.`
        : `You bought the ${name}! Tap your cat to wear it.`;
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

/** A thing's name in a sentence: "a party hat", "an orange collar". */
function aOrAn(name: string): string {
  const lower = name.toLowerCase();
  return `${/^[aeiou]/.test(lower) ? "an" : "a"} ${lower}`;
}

/**
 * What a picture of a cat shows, for a screen reader: "Kitty 1234: a calico cat wearing a party hat and a red collar",
 * or "Kitty 1234: a grey cat".
 */
export function describeCat(name: string, fur: string, hatName?: string | null, collarName?: string | null): string {
  const worn = [hatName, collarName].filter((worn): worn is string => !!worn).map(aOrAn);
  const furCat = aOrAn(`${fur} cat`);
  const wearing = worn.length > 0 ? ` wearing ${worn.join(" and ")}` : "";
  return `${name}: ${furCat}${wearing}`;
}

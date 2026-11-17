import { describe, expect, it } from "vitest";
import {
  catalogueRow, catalogueSections, describeCat, hasMoreToWear, isWorn, shopMessage, showsOutfit, wardrobeRow, wardrobeSections,
  wearingLine,
} from "./shop";
import type { Shop, ShopItem } from "./types";

const items: ShopItem[] = [
  { id: "party-hat", name: "Party hat", kind: "Hat", price: 30, furColor: null },
  { id: "crown", name: "Crown", kind: "Hat", price: 1000, furColor: null },
  { id: "red-collar", name: "Red collar", kind: "Collar", price: 20, furColor: null },
  { id: "fur-grey", name: "Grey fur", kind: "Fur", price: 75, furColor: "Grey" },
  { id: "fur-black", name: "Black fur", kind: "Fur", price: 75, furColor: "Black" },
];
function item(id: string): ShopItem {
  const found = items.find((i) => i.id === id);
  if (!found) throw new Error(`no ${id}`);
  return found;
}
const shop: Shop = {
  items, coins: 50, owned: ["fur-grey", "red-collar"], furColor: "Grey", hat: null, collar: null,
  counts: { "fur-grey": 1, "red-collar": 1 }, maxOfEachFurniture: 5,
};

describe("catalogueRow", () => {
  it("sells what the player can afford, at its price", () => {
    expect(catalogueRow(shop, item("party-hat"))).toEqual({
      item: item("party-hat"),
      status: "🪙 30",
      button: { action: "buy", label: "Buy", ariaLabel: "Buy the Party hat for 30 coins", primary: true, disabled: false },
    });
  });

  it("disables Buy for what the player can't afford, and says how many coins are still needed", () => {
    const row = catalogueRow(shop, item("crown"));
    expect(row.button).toMatchObject({ action: "buy", disabled: true, primary: false });
    expect(row.reason).toBe("need 950 more");
    expect(catalogueRow({ ...shop, coins: 29 }, item("party-hat")).reason).toBe("need 1 more");
    expect(catalogueRow({ ...shop, coins: 30 }, item("party-hat")).button?.disabled).toBe(false);
  });

  // What the player has is put on from their own card (the wardrobe), not the catalogue.
  it("sells nothing the player has: says it's theirs, or worn, with no button", () => {
    expect(catalogueRow(shop, item("red-collar"))).toEqual({ item: item("red-collar"), status: "Yours" });
    expect(catalogueRow({ ...shop, collar: "red-collar" }, item("red-collar"))).toEqual({ item: item("red-collar"), status: "Wearing" });
    expect(catalogueRow(shop, item("fur-grey"))).toEqual({ item: item("fur-grey"), status: "Your fur now" });
    expect(catalogueRow(shop, item("fur-black")).button?.action).toBe("buy");
  });
});

describe("wardrobeRow", () => {
  it("puts on what the player has, and takes off what the cat wears", () => {
    expect(wardrobeRow(shop, item("red-collar"))).toMatchObject({ status: "Yours", button: { action: "wear", ariaLabel: "Wear the Red collar" } });
    expect(wardrobeRow({ ...shop, collar: "red-collar" }, item("red-collar")))
      .toMatchObject({ status: "Wearing", button: { action: "takeOff", label: "Take off", ariaLabel: "Take off the Red collar" } });
  });

  it("has no button for the fur the cat has on: a cat always has one", () => {
    expect(wardrobeRow(shop, item("fur-grey"))).toEqual({ item: item("fur-grey"), status: "Your fur now" });
    expect(wardrobeRow({ ...shop, owned: [...shop.owned, "fur-black"] }, item("fur-black")).button?.action).toBe("wear");
  });
});

describe("wardrobeSections", () => {
  it("lists only what the player has to wear, in its parts, never furniture", () => {
    const sofa: ShopItem = { id: "sofa", name: "Sofa", kind: "Furniture", price: 350, furColor: null };
    const mine = { ...shop, items: [...items, sofa], owned: [...shop.owned, "sofa"] };

    expect(wardrobeSections(mine).map((s) => [s.title, s.rows.map((r) => r.item.id)])).toEqual([
      ["Collars", ["red-collar"]],
      ["Fur", ["fur-grey"]],
    ]);
  });

  it("says whether the player has anything to wear besides the coat on its cat", () => {
    expect(hasMoreToWear(shop)).toBe(true);
    expect(hasMoreToWear({ ...shop, owned: ["fur-grey"] })).toBe(false);
    // A hat worn is something besides the coat: the card shows it, to take off, and no hint to go shopping.
    expect(hasMoreToWear({ ...shop, owned: ["fur-grey", "party-hat"], hat: "party-hat" })).toBe(true);
  });
});

describe("describeCat", () => {
  it("says the cat's name, its fur and what it wears, for a screen reader", () => {
    expect(describeCat("Kitty 1234", "Calico", "Party hat", "Red collar")).toBe("Kitty 1234: a calico cat wearing a party hat and a red collar");
    expect(describeCat("Kitty 1234", "Grey")).toBe("Kitty 1234: a grey cat");
    expect(describeCat("Tom", "Orange", null, "Bell collar")).toBe("Tom: an orange cat wearing a bell collar");
    expect(describeCat("Tom", "Black", "Top hat", null)).toBe("Tom: a black cat wearing a top hat");
  });
});

describe("isWorn", () => {
  it("is the hat, the collar or the coat the cat has on", () => {
    const dressed = { ...shop, hat: "party-hat", furColor: "Black" as const };
    expect(items.filter((i) => isWorn(dressed, i)).map((i) => i.id)).toEqual(["party-hat", "fur-black"]);
  });
});

describe("showsOutfit", () => {
  // The tab that made a change already shows the shop as it is after; only a change it didn't make needs it fetched.
  it("is whether the shop already shows the cat dressed so", () => {
    const dressed = { ...shop, hat: "party-hat" };
    expect(showsOutfit(dressed, { playerId: "tom", furColor: "Grey", hat: "party-hat", collar: null })).toBe(true);
    expect(showsOutfit(dressed, { playerId: "tom", furColor: "Grey", hat: null, collar: null })).toBe(false);
    expect(showsOutfit(dressed, { playerId: "tom", furColor: "Black", hat: "party-hat", collar: null })).toBe(false);
    expect(showsOutfit(dressed, { playerId: "tom", furColor: "Grey", hat: "party-hat", collar: "red-collar" })).toBe(false);
  });
});

describe("shopSections", () => {
  it("puts hats, then collars, then fur, each in the server's order", () => {
    expect(catalogueSections(shop).map((s) => [s.title, s.rows.map((r) => r.item.id)])).toEqual([
      ["Hats", ["party-hat", "crown"]],
      ["Collars", ["red-collar"]],
      ["Fur", ["fur-grey", "fur-black"]],
    ]);
  });
});

describe("shopMessage", () => {
  it("says what the button did", () => {
    expect(shopMessage("buy", item("crown"))).toBe("You bought the Crown! Tap your cat to wear it.");
    expect(shopMessage("wear", item("crown"))).toBe("You're wearing the Crown.");
    expect(shopMessage("takeOff", item("crown"))).toBe("You took off the Crown.");
  });
});

describe("wearingLine", () => {
  it("names the hat and collar, or says nothing", () => {
    expect(wearingLine({ hatName: "Crown", collarName: "Bell collar" })).toBe("Wearing: Crown, Bell collar");
    expect(wearingLine({ hatName: null, collarName: "Bell collar" })).toBe("Wearing: Bell collar");
    expect(wearingLine({ hatName: null, collarName: null })).toBeUndefined();
    expect(wearingLine({})).toBeUndefined();
  });
});

describe("furniture in the shop", () => {
  const sofa: ShopItem = { id: "sofa", name: "Sofa", kind: "Furniture", price: 350, furColor: null };
  const withSofa = (count: number, coins = 400): Shop =>
    ({ ...shop, items: [...items, sofa], coins, owned: [...shop.owned, ...(count > 0 ? ["sofa"] : [])], counts: { sofa: count } });

  it("sells a piece the player hasn't got, and another of one they have, up to the most a cat keeps", () => {
    expect(catalogueRow(withSofa(0), sofa)).toMatchObject({ status: "🪙 350", button: { action: "buy", label: "Buy", ariaLabel: "Buy the Sofa for 350 coins" } });
    expect(catalogueRow(withSofa(2), sofa)).toMatchObject({
      status: "🪙 350 · you have 2", button: { action: "buy", label: "Buy another", ariaLabel: "Buy another Sofa for 350 coins", disabled: false },
    });
    expect(catalogueRow(withSofa(2, 100), sofa)).toMatchObject({ button: { disabled: true }, reason: "need 250 more" });
    expect(catalogueRow(withSofa(5), sofa)).toEqual({ item: sofa, status: "You have 5, as many as a cat can keep" });
  });

  it("keeps as many of a piece as the server says a cat can", () => {
    const fewer = { ...withSofa(3), maxOfEachFurniture: 3 } as Shop;

    expect(catalogueRow(fewer, sofa)).toEqual({ item: sofa, status: "You have 3, as many as a cat can keep" });
  });

  it("never wears furniture, and lists it in its own part after the fur", () => {
    expect(isWorn(withSofa(1), sofa)).toBe(false);
    expect(catalogueSections(withSofa(1)).map((s) => s.title)).toEqual(["Hats", "Collars", "Fur", "Furniture"]);
  });

  it("says where furniture bought goes", () => {
    expect(shopMessage("buy", sofa)).toBe("You bought the Sofa! Put it out at home: Friends, then Go home.");
  });
});

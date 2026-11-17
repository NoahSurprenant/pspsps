import { describe, expect, it } from "vitest";
import { isWorn, shopMessage, shopRow, shopSections, showsOutfit, wearingLine } from "./shop";
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
const shop: Shop = { items, coins: 50, owned: ["fur-grey", "red-collar"], furColor: "Grey", hat: null, collar: null };

describe("shopRow", () => {
  it("sells what the player can afford, at its price", () => {
    expect(shopRow(shop, item("party-hat"))).toEqual({
      item: item("party-hat"),
      status: "🪙 30",
      button: { action: "buy", label: "Buy", ariaLabel: "Buy the Party hat for 30 coins", primary: true, disabled: false },
    });
  });

  it("disables Buy for what the player can't afford, and says how many coins are still needed", () => {
    const row = shopRow(shop, item("crown"));
    expect(row.button).toMatchObject({ action: "buy", disabled: true, primary: false });
    expect(row.reason).toBe("need 950 more");
    expect(shopRow({ ...shop, coins: 29 }, item("party-hat")).reason).toBe("need 1 more");
    expect(shopRow({ ...shop, coins: 30 }, item("party-hat")).button?.disabled).toBe(false);
  });

  it("offers to put on what the player has, and to take off what the cat wears", () => {
    expect(shopRow(shop, item("red-collar"))).toMatchObject({ status: "Yours", button: { action: "wear", ariaLabel: "Wear the Red collar" } });
    expect(shopRow({ ...shop, collar: "red-collar" }, item("red-collar")))
      .toMatchObject({ status: "Wearing", button: { action: "takeOff", label: "Take off", ariaLabel: "Take off the Red collar" } });
  });

  it("has no button for the fur the cat has on: a cat always has one", () => {
    expect(shopRow(shop, item("fur-grey"))).toEqual({ item: item("fur-grey"), status: "Your fur now" });
    expect(shopRow(shop, item("fur-black")).button?.action).toBe("buy");
    expect(shopRow({ ...shop, owned: [...shop.owned, "fur-black"] }, item("fur-black")).button?.action).toBe("wear");
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
    expect(shopSections(shop).map((s) => [s.title, s.rows.map((r) => r.item.id)])).toEqual([
      ["Hats", ["party-hat", "crown"]],
      ["Collars", ["red-collar"]],
      ["Fur", ["fur-grey", "fur-black"]],
    ]);
  });
});

describe("shopMessage", () => {
  it("says what the button did", () => {
    expect(shopMessage("buy", item("crown"))).toBe("You bought the Crown! Wear it whenever you like.");
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

import { describe, expect, it } from "vitest";
import {
  canVisitFromCard, countOf, decoratingHint, furnitureLeft, isOwnHome, newer, pieceAt, propOf, propsOf, visitorChoices,
  visitorsMessage,
} from "./home";
import type { FurnitureView, HomeView, Shop, ShopItem } from "./types";

const items: ShopItem[] = [
  { id: "party-hat", name: "Party hat", kind: "Hat", price: 30, furColor: null },
  { id: "plant", name: "Potted plant", kind: "Furniture", price: 35, furColor: null },
  { id: "cat-bed", name: "Cat bed", kind: "Furniture", price: 70, furColor: null },
  { id: "sofa", name: "Sofa", kind: "Furniture", price: 350, furColor: null },
];
const shop: Shop = {
  items, coins: 0, owned: ["fur-grey", "cat-bed", "plant"], furColor: "Grey", hat: null, collar: null,
  counts: { "fur-grey": 1, "cat-bed": 1, plant: 3 }, maxOfEachFurniture: 5,
};
const bed: FurnitureView = { id: "b1", itemId: "cat-bed", name: "Cat bed", x: 2, z: -1, radius: 0.6, quarterTurns: 1 };
const plant: FurnitureView = { id: "p1", itemId: "plant", name: "Potted plant", x: 2.9, z: -1, radius: 0.5, quarterTurns: 0 };
const home: HomeView = { ownerId: "tom", ownerName: "Tom", visitors: "Friends", furniture: [bed, plant], version: 3 };

describe("propOf and propsOf", () => {
  it("draws a piece as a prop of its kind, turned its quarters", () => {
    expect(propOf(bed)).toEqual({ kind: "cat-bed", x: 2, z: -1, radius: 0.6, facing: Math.PI / 2 });
    expect(propsOf(home).map((p) => p.kind)).toEqual(["cat-bed", "plant"]);
  });
});

describe("newer", () => {
  it("keeps the newer of two views of the same home", () => {
    const older = { ...home, version: 2, furniture: [] };

    expect(newer(home, older)).toBe(home);
    expect(newer(older, home)).toBe(home);
    expect(newer(undefined, older)).toBe(older);
    expect(newer({ ...home, ownerId: "felix" }, older)).toBe(older);
  });

  // The owner's tab hears its own change twice (its answer, and the room's broadcast): drawn once.
  it("keeps the one it has for the same version", () => {
    expect(newer(home, { ...home, furniture: [] })).toBe(home);
  });
});

describe("countOf and furnitureLeft", () => {
  it("counts what the player has", () => {
    expect(countOf(shop, "plant")).toBe(3);
    expect(countOf(shop, "sofa")).toBe(0);
  });

  it("lists the furniture the player has, in the shop's order, with how many are left to put out", () => {
    expect(furnitureLeft(shop, home).map(({ item, owned, out, left }) => [item.id, owned, out, left])).toEqual([
      ["plant", 3, 1, 2],
      ["cat-bed", 1, 1, 0],
    ]);
  });

});

describe("pieceAt", () => {
  it("finds the piece whose circle holds the spot, the nearest when two do", () => {
    expect(pieceAt(home.furniture, 2.1, -1.1)).toBe(bed);
    expect(pieceAt(home.furniture, 2.5, -1)).toBe(plant);
    expect(pieceAt(home.furniture, 0, 0)).toBeUndefined();
  });
});

describe("decoratingHint", () => {
  const left = furnitureLeft(shop, home);

  it("says what to do next: with a piece selected, with furniture picked, and with neither", () => {
    expect(decoratingHint({ selected: "b1" }, left, home)).toBe("The Cat bed: turn it, or put it away.");
    expect(decoratingHint({ picked: "plant" }, left, home)).toBe("Tap the floor where the Potted plant goes, or find it a spot.");
    expect(decoratingHint({}, left, home)).toBe("Pick something to put out, or tap a piece in the room to turn it or put it away.");
    expect(decoratingHint({}, left, { ...home, furniture: [] })).toBe("Pick something to put out.");
  });

  it("sends a player with no furniture to the shop", () => {
    expect(decoratingHint({}, [], { ...home, furniture: [] })).toBe("You have no furniture yet. Buy some in the shop.");
  });
});

describe("canVisitFromCard", () => {
  const card = { playerId: "felix", displayName: "Felix", furColor: "Black" as const, friendState: "Friends" as const, canVisitHome: true };

  it("offers a home that lets the player in, unless the player is in it already", () => {
    expect(canVisitFromCard(card, undefined)).toBe(true);
    expect(canVisitFromCard(card, "tom")).toBe(true);
    expect(canVisitFromCard(card, "felix")).toBe(false);
    expect(canVisitFromCard({ ...card, canVisitHome: false }, undefined)).toBe(false);
  });
});

describe("visitors", () => {
  it("offers the server's three choices, and says each", () => {
    expect(visitorChoices.map((c) => c.value)).toEqual(["Friends", "Anyone", "Nobody"]);
    expect(visitorsMessage("Nobody")).toBe("Nobody can visit now.");
    expect(visitorsMessage("Anyone")).toBe("Any cat can visit now.");
  });

  it("knows the player's own home", () => {
    expect(isOwnHome(home, "tom")).toBe(true);
    expect(isOwnHome(home, "felix")).toBe(false);
    expect(isOwnHome(null, "tom")).toBe(false);
  });
});

import { NullEngine } from "@babylonjs/core";
import { describe, expect, it } from "vitest";
import { loadCatModel } from "./cat";
import catGlb from "./models/cat.glb?inline";
import { outfitKey, Portraits } from "./portrait";

/** The real cat model's bytes, from the data URL Vite makes of it. */
function modelBytes(): Uint8Array {
  return Uint8Array.from(atob(catGlb.slice(catGlb.indexOf(",") + 1)), (c) => c.charCodeAt(0));
}

/** Portraits whose "picture" is what the cat sat for it wearing, and how many were taken. */
function studio() {
  let taken = 0;
  const portraits: Portraits = new Portraits(new NullEngine(), (scene) => loadCatModel(scene, modelBytes()), () => {
    taken++;
    return Promise.resolve(JSON.stringify(portraits.lastSitter?.wearing));
  });
  return { portraits, taken: () => taken };
}

describe("Portraits", () => {
  // A card shows the cat as it looks now: a change of outfit is a new picture.
  it("takes the picture of the cat dressed as asked, and a new one when the outfit changes", async () => {
    const { portraits, taken } = studio();

    const partyHat = await portraits.picture({ fur: "Calico", hat: "party-hat", collar: null });
    const withCollar = await portraits.picture({ fur: "Calico", hat: "party-hat", collar: "red-collar" });
    const blackCoat = await portraits.picture({ fur: "Black", hat: null, collar: null });

    expect(JSON.parse(partyHat)).toEqual({ fur: "Calico", hat: "party-hat" });
    expect(JSON.parse(withCollar)).toEqual({ fur: "Calico", hat: "party-hat", collar: "red-collar" });
    expect(JSON.parse(blackCoat)).toEqual({ fur: "Black" });
    expect(taken()).toBe(3);
  });

  it("keeps a picture: the same outfit again is not taken again, also when asked for twice at once", async () => {
    const { portraits, taken } = studio();
    const outfit = { fur: "Grey" as const, hat: "crown", collar: null };

    const [first, second] = await Promise.all([portraits.picture(outfit), portraits.picture({ ...outfit })]);

    expect(second).toBe(first);
    expect(portraits.cached(outfit)).toBe(first);
    expect(taken()).toBe(1);
  });

  it("keys an outfit by its fur, hat and collar", () => {
    expect(outfitKey({ fur: "Grey", hat: null, collar: null })).toBe("Grey//");
    expect(outfitKey({ fur: "Grey", hat: "bow", collar: "red-collar" })).toBe("Grey/bow/red-collar");
  });
});

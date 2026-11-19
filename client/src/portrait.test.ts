import { NullEngine } from "@babylonjs/core";
import { describe, expect, it } from "vitest";
import { loadCatModel } from "./cat";
import { outfitKey, Portraits } from "./portrait";
import { modelBytes } from "./testCatModel";

/** Portraits whose "picture" is what the cat sat for it wearing, and how many were taken. */
function studio() {
  let taken = 0;
  const portraits = new Portraits(new NullEngine(), (scene) => loadCatModel(scene, modelBytes()), (_scene, _camera, _size, sitter) => {
    taken++;
    return Promise.resolve(JSON.stringify(sitter.wearing));
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

  // A card opened again shows at once: the pictures kept are the ones used last, not the ones taken first.
  it("keeps the pictures used last, dropping the one unused longest", async () => {
    const { portraits, taken } = studio();
    const outfit = (i: number) => ({ fur: "Grey" as const, hat: null, collar: `collar-${i}` });
    for (let i = 0; i < 24; i++) {
      await portraits.picture(outfit(i));
    }

    // The first one is shown again, then one more is taken: the second goes, not the first.
    await portraits.picture(outfit(0));
    await portraits.picture(outfit(24));

    expect(portraits.cached(outfit(0))).toBeDefined();
    expect(portraits.cached(outfit(1))).toBeUndefined();
    expect(taken()).toBe(25);
  });

  it("keys an outfit by its fur, hat and collar", () => {
    expect(outfitKey({ fur: "Grey", hat: null, collar: null })).toBe("Grey//");
    expect(outfitKey({ fur: "Grey", hat: "bow", collar: "red-collar" })).toBe("Grey/bow/red-collar");
  });
});

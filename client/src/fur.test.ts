import { describe, expect, it } from "vitest";
import { coatOf } from "./fur";
import type { FurColor } from "./types";

describe("coatOf", () => {
  it("has a coat for every fur colour", () => {
    const furs: FurColor[] = ["Orange", "Black", "White", "Grey", "Cream", "Brown", "Calico", "Tuxedo"];

    for (const fur of furs) {
      expect(coatOf(fur).body).toMatch(/^#[0-9a-f]{6}$/);
    }
  });

  it("falls back to grey for a colour this client doesn't know yet", () => {
    expect(coatOf("Purple" as FurColor)).toEqual(coatOf("Grey"));
  });
});

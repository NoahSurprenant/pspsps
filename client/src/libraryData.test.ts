import { describe, expect, it } from "vitest";
import { blankVariant, newPainterTemplate, newWildlifeTemplate, recipeBody, recipeFrom, variantBody, withVariantFloor } from "./libraryData";

describe("libraryData", () => {
  it("saves a painter by surface and steps, a creature by parts and motion", () => {
    expect(recipeBody("painters", { id: "a", surface: "floor", steps: [], extra: 1 })).toEqual({ surface: "floor", steps: [] });
    expect(recipeBody("wildlife", { id: "a", parts: [], motion: null, extra: 1 })).toEqual({ parts: [], motion: null });
  });

  it("the starters pass their own check", () => {
    expect(recipeFrom(JSON.stringify(newPainterTemplate()), "painters")).toHaveProperty("entry");
    expect(recipeFrom(JSON.stringify(newWildlifeTemplate()), "wildlife")).toHaveProperty("entry");
  });

  it.each([
    ["{", "painters", "valid JSON"], ["[]", "painters", "one JSON object"], ["{}", "wildlife", "an id"],
    ['{"id":"x","surface":"floor"}', "painters", "needs steps"], ['{"id":"x"}', "wildlife", "needs parts"],
  ] as const)("refuses %s", (text, kind, words) => {
    expect(recipeFrom(text, kind)).toMatchObject({ problem: expect.stringContaining(words) });
  });

  it("a variant has colours, and a floor only when it has a painter", () => {
    const variant = blankVariant("winter");
    const iced = withVariantFloor(variant, { painter: "tiles", colour: "#aaccee" });

    expect(variantBody(variant)).toEqual({ sky: variant.sky, wallColour: variant.wallColour });
    expect(variantBody(iced).floor).toEqual({ painter: "tiles", colour: "#aaccee" });
    expect(withVariantFloor(iced, { painter: "" }).floor).toBeUndefined();
  });
});

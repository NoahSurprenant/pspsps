import { describe, expect, it } from "vitest";
import { newLookTemplate, optionsText, parseJson, problemsText, publishQuery, roomLine } from "./roomsAdmin";

describe("roomLine", () => {
  it("says what is live and whether a draft waits", () => {
    expect(roomLine({ roomId: "garden", name: "Garden", published: 2, draft: null })).toBe("Garden (version 2 live)");
    expect(roomLine({ roomId: "garden", name: "Garden", published: 2, draft: 3 })).toBe("Garden (version 2 live, draft 3)");
    expect(roomLine({ roomId: "attic", name: "Attic", published: null, draft: 1 })).toBe("Attic (not published, draft 1)");
  });
});

describe("parseJson", () => {
  it("reads JSON and gives null for anything else", () => {
    expect(parseJson('{"a":1}')).toEqual({ a: 1 });
    expect(parseJson("{nope")).toBeNull();
  });
});

describe("problemsText", () => {
  it("says it can be published, or what stops it", () => {
    expect(problemsText([])).toContain("can be published");
    expect(problemsText(["a.", "b."])).toContain("a. b.");
  });
});

describe("publishQuery", () => {
  it("asks to publish with the rooms named, however they are separated", () => {
    expect(publishQuery("")).toBe("");
    expect(publishQuery(" loft, attic  cellar ")).toBe("?with=loft&with=attic&with=cellar");
  });
});

describe("the editor's aids", () => {
  it("lists the names a look may use", () => {
    const text = optionsText({ propKinds: ["rug", "tree"], floors: ["planks"], walls: ["bricks"], grounds: ["street"], generators: ["town"], wildlife: ["fish"] });

    expect(text).toContain("Prop kinds: rug, tree");
    expect(text).toContain("Wildlife: fish");
  });

  it("starts a new look from a floor and a wall that exist", () => {
    expect(newLookTemplate()).toMatchObject({ floor: { painter: "planks" }, wall: { painter: "panelled" } });
  });
});

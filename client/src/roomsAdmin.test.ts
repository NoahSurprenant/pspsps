import { describe, expect, it } from "vitest";
import { parseJson, problemsText, roomLine } from "./roomsAdmin";

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

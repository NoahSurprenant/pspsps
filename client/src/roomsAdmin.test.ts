import { describe, expect, it } from "vitest";
import { auditText, draftQuery, draftSetUrl, goLiveInstant, goLiveText, exportUrl, importText, newLookTemplate, optionsText, parseJson, problemsText, publishQuery, roomLine } from "./roomsAdmin";

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

describe("scheduled publishing", () => {
  it("adds the go-live instant to the query, with or without rooms riding along", () => {
    expect(publishQuery("", "2026-12-01T10:00:00.000Z")).toBe("?at=2026-12-01T10%3A00%3A00.000Z");
    expect(publishQuery("loft", "2026-12-01T10:00:00.000Z")).toBe("?with=loft&at=2026-12-01T10%3A00%3A00.000Z");
  });

  it("turns a date-time field into an instant, or nothing when it is empty or not a time", () => {
    expect(goLiveInstant("2026-12-01T10:00")).toBe(new Date("2026-12-01T10:00").toISOString());
    expect(goLiveInstant("")).toBeNull();
    expect(goLiveInstant("soon")).toBeNull();
  });

  it("publishes some drafts as a set: the first is the room, the rest ride along", () => {
    expect(draftSetUrl([])).toBeNull();
    expect(draftSetUrl(["garden"])).toEqual({ roomId: "garden", query: "" });
    expect(draftSetUrl(["garden", "loft", "attic"], "2026-12-01T10:00:00.000Z")).toEqual({
      roomId: "garden",
      query: "?with=loft&with=attic&at=2026-12-01T10%3A00%3A00.000Z",
    });
  });

  it("says in the room's line when its draft is waiting for its time", () => {
    expect(goLiveText(null)).toBe("");
    expect(roomLine({ roomId: "garden", name: "Garden", published: 2, draft: 3, goLiveAt: "2026-12-01T10:00:00.000Z" })).toContain("draft 3, goes live");
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

describe("saving over a draft", () => {
  it("names the revision it was opened at, and none for a new draft", () => {
    expect(draftQuery(3)).toBe("?revision=3");
    expect(draftQuery(null)).toBe("");
    expect(draftQuery(undefined)).toBe("");
  });

  it("reads an audit line as when, what and to which room", () => {
    expect(auditText({ at: "2026-11-20T18:05:33+00:00", adminId: null, action: "published", target: "garden", detail: "version 2" }))
      .toBe("2026-11-20 18:05 published garden (version 2)");
    expect(auditText({ at: "2026-11-20T18:05:33+00:00", adminId: null, action: "look-saved", target: "cafe", detail: "" }))
      .toBe("2026-11-20 18:05 look-saved cafe");
  });

  it("downloads the content from the admin export", () => {
    expect(exportUrl).toBe("/api/admin/export");
  });

  it("says what loading the content file did", () => {
    expect(importText({ applied: ["room a: saved as a draft"], unchanged: [], problems: ["look b: differs"] })).toBe("room a: saved as a draft\nNot applied: look b: differs");
    expect(importText({ applied: [], unchanged: ["room a"], problems: [] })).toBe("Nothing to load: 1 items already match.");
    expect(importText({ applied: [], unchanged: [], problems: [], pending: ["overlay a/b: waits"] })).toBe("Waiting: overlay a/b: waits");
    expect(importText(null)).toBe("Nothing came back.");
  });
});

import { describe, expect, it } from "vitest";
import { canGoTo, friendButtons, friendStatus, sections, whereIs } from "./friends";
import type { Friend } from "./types";

const felix: Friend = {
  playerId: "felix",
  displayName: "Felix",
  furColor: "Black",
  friendState: "Friends",
  place: { worldId: "calico", worldName: "Calico", roomId: "cafe", roomName: "Cat Café" },
};

describe("friendButtons", () => {
  it("offers to add a cat with nothing between you", () => {
    expect(friendButtons("None").map((b) => b.label)).toEqual(["Add friend"]);
  });

  it("lets the one asked accept or decline, and the one who asked take it back", () => {
    expect(friendButtons("RequestReceived").map((b) => b.action)).toEqual(["accept", "decline"]);
    expect(friendButtons("RequestSent").map((b) => b.action)).toEqual(["cancel"]);
  });

  it("lets friends end it", () => {
    expect(friendButtons("Friends").map((b) => b.action)).toEqual(["remove"]);
    expect(friendStatus("Friends")).toBe("You're friends.");
    expect(friendStatus("None")).toBeUndefined();
  });
});

describe("whereIs and canGoTo", () => {
  it("names a friend's room and world, and lets the player go there", () => {
    const here = { worldId: "tabby", roomId: "cafe" };

    expect(whereIs(felix, here)).toBe("Cat Café, Calico");
    expect(canGoTo(felix, here)).toBe(true);
  });

  it("says a friend in the same room is here, with nowhere to go", () => {
    const here = { worldId: "calico", roomId: "cafe" };

    expect(whereIs(felix, here)).toBe("Here with you");
    expect(canGoTo(felix, here)).toBe(false);
  });

  it("says an offline friend is offline, with nowhere to go", () => {
    const offline = { ...felix, place: null };

    expect(whereIs(offline, undefined)).toBe("Offline");
    expect(canGoTo(offline, undefined)).toBe(false);
  });

  it("never offers to go to a request", () => {
    expect(canGoTo({ ...felix, friendState: "RequestReceived" }, undefined)).toBe(false);
  });
});

describe("sections", () => {
  it("splits the list into friends, requests to the player, and the player's", () => {
    const asker = { ...felix, playerId: "asker", friendState: "RequestReceived" as const, place: null };
    const asked = { ...felix, playerId: "asked", friendState: "RequestSent" as const, place: null };

    expect(sections([felix, asker, asked])).toEqual({ friends: [felix], received: [asker], sent: [asked] });
  });
});

describe("a friend at home", () => {
  const atHome: Friend = {
    ...felix,
    place: { worldId: "", worldName: "", roomId: "home", roomName: "Felix's home", homeOwnerId: "felix" },
  };

  it("is in their home, which is in no world", () => {
    expect(whereIs(atHome, { worldId: "tabby", roomId: "town-square" })).toBe("Felix's home");
  });

  it("is here with the player in the same home, whichever world each came from", () => {
    expect(whereIs(atHome, { worldId: "calico", roomId: "home", homeOwnerId: "felix" })).toBe("Here with you");
    expect(canGoTo(atHome, { worldId: "calico", roomId: "home", homeOwnerId: "felix" })).toBe(false);
  });

  it("isn't here with a player in another home, or in a world's room", () => {
    expect(canGoTo(atHome, { worldId: "", roomId: "home", homeOwnerId: "tom" })).toBe(true);
    expect(canGoTo(felix, { worldId: "calico", roomId: "home", homeOwnerId: "felix" })).toBe(true);
  });
});

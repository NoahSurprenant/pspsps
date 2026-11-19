import { describe, expect, it } from "vitest";
import { comeBack, saysSentOutAgain, stayAfterRefusal, walkBackIn } from "./rejoin";

describe("walkBackIn", () => {
  function recorder(arrivesIn: string | undefined) {
    const calls: string[] = [];
    return {
      calls,
      enter: (worldId: string) => {
        calls.push(`enter ${worldId}`);
        return Promise.resolve(arrivesIn);
      },
      goTo: (roomId: string) => {
        calls.push(`goTo ${roomId}`);
        return Promise.resolve();
      },
    };
  }

  it("enters the world again, then goes back to the room it was in", async () => {
    const { calls, enter, goTo } = recorder("town-square");

    await walkBackIn({ worldId: "tabby", roomId: "cafe" }, enter, goTo);

    expect(calls).toEqual(["enter tabby", "goTo cafe"]);
  });

  it("stays when entering the world lands in that room", async () => {
    const { calls, enter, goTo } = recorder("town-square");

    await walkBackIn({ worldId: "tabby", roomId: "town-square" }, enter, goTo);

    expect(calls).toEqual(["enter tabby"]);
  });

  it("stops when entering the world fails", async () => {
    const { calls, enter, goTo } = recorder(undefined);

    await walkBackIn({ worldId: "tabby", roomId: "cafe" }, enter, goTo);

    expect(calls).toEqual(["enter tabby"]);
  });
});

describe("comeBack", () => {
  it("after a lost room, only goes back to the room: the tab is still in its world", async () => {
    const calls: string[] = [];

    await comeBack(
      "roomLost",
      { worldId: "tabby", roomId: "cafe" },
      (worldId) => {
        calls.push(`enter ${worldId}`);
        return Promise.resolve("town-square");
      },
      (roomId) => {
        calls.push(`goTo ${roomId}`);
        return Promise.resolve();
      },
    );

    expect(calls).toEqual(["goTo cafe"]);
  });

  it("after a reconnect, walks back in through the world", async () => {
    const calls: string[] = [];

    await comeBack(
      "reconnected",
      { worldId: "tabby", roomId: "cafe" },
      (worldId) => {
        calls.push(`enter ${worldId}`);
        return Promise.resolve("town-square");
      },
      (roomId) => {
        calls.push(`goTo ${roomId}`);
        return Promise.resolve();
      },
    );

    expect(calls).toEqual(["enter tabby", "goTo cafe"]);
  });
});

describe("saysSentOutAgain", () => {
  const home = { ownerId: "me", ownerName: "Me", visitors: "Friends" as const, furniture: [], version: 0 };

  it("says it again in Town Square, where the message said the cat went", () => {
    expect(saysSentOutAgain({ home: null, notice: null })).toBe(true);
  });

  it("doesn't when the cat landed somewhere else, and was told where and why", () => {
    expect(saysSentOutAgain({ home: null, notice: "Town Square is full. You went to Cat Café instead." })).toBe(false);
    expect(saysSentOutAgain({ home, notice: "Town Square is full. You went home instead." })).toBe(false);
  });

  it("doesn't when no room shows (the walk failed, and said why)", () => {
    expect(saysSentOutAgain(undefined)).toBe(false);
  });
});

describe("stayAfterRefusal", () => {
  // After a reconnect, entering the world put the cat in a room, which the server keeps it in when the room it was in is
  // full now: the page goes on showing that one, not no room.
  it("keeps showing the room entering the world landed in, after a reconnect", () => {
    expect(stayAfterRefusal("reconnected", "town square")).toBe("town square");
  });

  // After a lost room the server has the cat in no room: a refused walk back shows none (it never sends RoomLost again
  // for a room the tab isn't in, so the page would show it forever, its moves refused).
  it("shows no room after a lost room's walk back is refused", () => {
    expect(stayAfterRefusal("roomLost", "the lost room")).toBeUndefined();
  });
});

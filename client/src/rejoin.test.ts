import { describe, expect, it } from "vitest";
import { comeBack, walkBackIn } from "./rejoin";

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

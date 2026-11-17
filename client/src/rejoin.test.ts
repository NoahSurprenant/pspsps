import { describe, expect, it } from "vitest";
import { walkBackIn } from "./rejoin";

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

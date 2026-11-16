import { describe, expect, it } from "vitest";
import { Room } from "./room";
import type { CatView, RoomSnapshot } from "./types";

const tom: CatView = { playerId: "tom", displayName: "Tom", furColor: "Grey", x: 0, z: 4 };
const felix: CatView = { playerId: "felix", displayName: "Felix", furColor: "Tuxedo", x: 1, z: 1 };

function snapshot(cats: CatView[]): RoomSnapshot {
  return { worldId: "tabby", roomId: "town-square", roomName: "Town Square", width: 24, depth: 16, cats, chat: [], rooms: [] };
}

describe("Room", () => {
  it("treats a join for a cat already in the snapshot as an update", () => {
    const room = new Room(snapshot([tom]));

    room.join({ ...tom, x: 3 });

    expect([...room.cats.values()]).toEqual([{ ...tom, x: 3 }]);
  });

  it("moves known cats and ignores unknown ones", () => {
    const room = new Room(snapshot([tom]));

    expect(room.move("tom", 5, -2)).toEqual({ ...tom, x: 5, z: -2 });
    expect(room.move("felix", 1, 1)).toBeUndefined();
    expect(room.cats.has("felix")).toBe(false);
  });

  it("forgets cats that leave", () => {
    const room = new Room(snapshot([tom, felix]));

    room.leave("tom");
    room.leave("tom");

    expect([...room.cats.keys()]).toEqual(["felix"]);
    expect(room.nameOf("tom")).toBe("A cat");
  });

  it("keeps only the latest chat lines", () => {
    const room = new Room(snapshot([tom]));

    for (let i = 0; i < Room.chatHistory + 3; i++) {
      room.say({ playerId: "tom", text: `line ${i}`, at: "" });
    }

    expect(room.chat).toHaveLength(Room.chatHistory);
    expect(room.chat.at(-1)?.text).toBe(`line ${Room.chatHistory + 2}`);
  });
});

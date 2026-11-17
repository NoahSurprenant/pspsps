import { describe, expect, it } from "vitest";
import { Room } from "./room";
import type { CatView, RoomSnapshot } from "./types";

const tom: CatView = { playerId: "tom", displayName: "Tom", furColor: "Grey", x: 0, z: 4, pose: null, hat: null, collar: null };
const felix: CatView = { playerId: "felix", displayName: "Felix", furColor: "Tuxedo", x: 1, z: 1, pose: null, hat: null, collar: null };

function snapshot(cats: CatView[]): RoomSnapshot {
  return { worldId: "tabby", roomId: "town-square", roomName: "Town Square", width: 24, depth: 16, cats, chat: [], rooms: [], pond: null, doors: [], props: [] };
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

  it("dresses a cat that's here, keeping where it stands, and ignores one that isn't", () => {
    const room = new Room(snapshot([tom]));

    expect(room.dress({ playerId: "tom", furColor: "Calico", hat: "crown", collar: null })).toBe(true);
    expect(room.dress({ playerId: "felix", furColor: "Grey", hat: "bow", collar: null })).toBe(false);

    expect([...room.cats.values()]).toEqual([{ ...tom, furColor: "Calico", hat: "crown", collar: null }]);
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

  it("ignores a chat line it already has, and lines from cats that aren't here", () => {
    const line = { playerId: "tom", text: "hi", at: "2026-11-17T12:00:00Z" };
    const room = new Room({ ...snapshot([tom]), chat: [line] });

    room.say({ ...line });
    room.say({ playerId: "garfield", text: "from the old room", at: "2026-11-17T12:00:01Z" });
    room.say({ ...line, at: "2026-11-17T12:00:02Z" });

    expect(room.chat.map((l) => l.at)).toEqual(["2026-11-17T12:00:00Z", "2026-11-17T12:00:02Z"]);
  });

  it("keeps each cat's latest emote and forgets it when the cat walks or leaves", () => {
    const room = new Room(snapshot([tom, felix]));

    room.emote({ playerId: "tom", emote: "sit", at: "" });
    room.emote({ playerId: "tom", emote: "wave", at: "" });
    room.emote({ playerId: "felix", emote: "meow", at: "" });
    room.emote({ playerId: "garfield", emote: "sleep", at: "" });

    expect(Object.fromEntries([...room.emotes].map(([id, e]) => [id, e.emote]))).toEqual({ tom: "wave", felix: "meow" });
    room.move("felix", 2, 2);
    room.leave("tom");
    expect([...room.emotes.keys()]).toEqual([]);
  });

  it("knows a cat's held pose from the snapshot and from sit and sleep, until the cat walks", () => {
    const room = new Room(snapshot([{ ...tom, pose: "sleep" }, felix]));
    expect(room.cats.get("tom")?.pose).toBe("sleep");

    room.emote({ playerId: "felix", emote: "sit", at: "" });
    room.emote({ playerId: "felix", emote: "meow", at: "" });
    expect(room.cats.get("felix")?.pose).toBe("sit");
    room.emote({ playerId: "tom", emote: "wave", at: "" });
    expect(room.cats.get("tom")?.pose).toBe("sleep");

    room.move("tom", 3, 3);
    expect(room.cats.get("tom")?.pose).toBeNull();
  });
});

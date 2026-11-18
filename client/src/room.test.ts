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

describe("a home's room", () => {
  const home = { ownerId: "tom", ownerName: "Tom", visitors: "Friends" as const, furniture: [], version: 1 };
  const piece = { id: "s1", itemId: "sofa", name: "Sofa", x: 1, z: -1, radius: 1.2, quarterTurns: 2 };

  it("draws the furniture as its owner changes it, keeping the newer of two changes", () => {
    const room = new Room({ ...snapshot([tom]), roomId: "home", home });

    expect(room.furnish({ ...home, furniture: [piece], version: 3 })).toBe(true);
    expect(room.furnish({ ...home, version: 2 })).toBe(false);
    expect(room.snapshot.props).toEqual([{ kind: "sofa", x: 1, z: -1, radius: 1.2, facing: Math.PI }]);
    expect(room.snapshot.home?.version).toBe(3);
  });

  it("ignores a home's change in a world's room, or another's home", () => {
    expect(new Room(snapshot([tom])).furnish(home)).toBe(false);
    expect(new Room({ ...snapshot([tom]), home }).furnish({ ...home, ownerId: "felix" })).toBe(false);
  });
});

describe("Room with muted cats", () => {
  const line = (playerId: string, text: string, at: string) => ({ playerId, text, at });

  it("keeps no lines of a muted cat from the room's history", () => {
    const history = [line("felix", "hi", "1"), line("tom", "hello", "2"), line("felix", "go away", "3")];
    const room = new Room({ ...snapshot([tom, felix]), chat: history }, (id) => id === "felix");

    expect(room.chat).toEqual([line("tom", "hello", "2")]);
  });

  it("refuses a muted cat's new lines, and takes everyone else's", () => {
    const room = new Room(snapshot([tom, felix]), (id) => id === "felix");

    expect(room.say(line("felix", "hi", "1"))).toBe(false);
    expect(room.say(line("tom", "hello", "2"))).toBe(true);

    expect(room.chat.map((l) => l.text)).toEqual(["hello"]);
  });

  it("forgets what a cat said once it is muted, and keeps what it says once unmuted", () => {
    const muted = new Set<string>();
    const room = new Room(snapshot([tom, felix]), (id) => muted.has(id));
    room.say(line("felix", "hi", "1"));
    room.say(line("tom", "hello", "2"));

    muted.add("felix");
    room.hush("felix");
    expect(room.chat.map((l) => l.text)).toEqual(["hello"]);

    muted.delete("felix");
    expect(room.say(line("felix", "sorry", "3"))).toBe(true);
    expect(room.chat.map((l) => l.text)).toEqual(["hello", "sorry"]);
  });
});

describe("Room's snapshot with muted cats", () => {
  it("keeps no muted cat's lines in the snapshot either, as it arrived or once muted", () => {
    const muted = new Set(["felix"]);
    const history = [
      { playerId: "felix", text: "hi", at: "1" },
      { playerId: "tom", text: "hello", at: "2" },
      { playerId: "garfield", text: "hey", at: "3" },
    ];
    const room = new Room({ ...snapshot([tom, felix]), chat: history }, (id) => muted.has(id));

    expect(room.snapshot.chat.map((l) => l.text)).toEqual(["hello", "hey"]);

    muted.add("garfield");
    room.hush("garfield");
    expect(room.snapshot.chat.map((l) => l.text)).toEqual(["hello"]);
  });
});

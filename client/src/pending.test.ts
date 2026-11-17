import { describe, expect, it } from "vitest";
import { loadRoom, PendingEvents } from "./pending";
import { Room } from "./room";
import type { CatView, ChatView, RoomSnapshot } from "./types";

const tom: CatView = { playerId: "tom", displayName: "Tom", furColor: "Grey", x: 0, z: 4, pose: null, hat: null, collar: null };
const felix: CatView = { playerId: "felix", displayName: "Felix", furColor: "Tuxedo", x: 1, z: 1, pose: null, hat: null, collar: null };
const hello: ChatView = { playerId: "felix", text: "hello", at: "2026-11-17T12:00:00Z" };

function snapshot(cats: CatView[], chat: ChatView[] = []): RoomSnapshot {
  return { worldId: "tabby", roomId: "town-square", roomName: "Town Square", width: 24, depth: 16, cats, chat, rooms: [], pond: null, doors: [], props: [] };
}

describe("PendingEvents", () => {
  it("lets events through while no room is loading", () => {
    const pending = new PendingEvents<string>();

    expect(pending.hold("moved")).toBe(false);
    expect(pending.finish()).toEqual([]);
  });

  it("holds events while a room loads and hands them back in order, once", () => {
    const pending = new PendingEvents<string>();
    pending.start();

    expect(pending.hold("joined")).toBe(true);
    expect(pending.hold("emoted")).toBe(true);

    expect(pending.finish()).toEqual(["joined", "emoted"]);
    expect(pending.hold("moved")).toBe(false);
    expect(pending.finish()).toEqual([]);
  });

  it("drops what it held when entering fails, and starts afresh for the next room", () => {
    const pending = new PendingEvents<string>();
    pending.start();
    pending.hold("old");

    pending.cancel();
    expect(pending.hold("after")).toBe(false);

    pending.start();
    pending.hold("new");
    expect(pending.finish()).toEqual(["new"]);
  });

  // The hub joins the room's group before the snapshot is taken, so some held events are already in the snapshot.
  it("replays onto the new room without doubling what the snapshot already has", () => {
    const pending = new PendingEvents<(room: Room) => void>();
    pending.start();
    pending.hold((room) => room.join(felix));
    pending.hold((room) => room.say(hello));
    pending.hold((room) => room.emote({ playerId: "felix", emote: "sleep", at: "" }));

    const room = new Room(snapshot([tom, felix], [hello]));
    for (const apply of pending.finish()) {
      apply(room);
    }

    expect([...room.cats.keys()]).toEqual(["tom", "felix"]);
    expect(room.chat).toEqual([hello]);
    expect(room.cats.get("felix")?.pose).toBe("sleep");
  });
});

describe("loadRoom", () => {
  it("replays what was held onto the room that loads", async () => {
    const pending = new PendingEvents<(room: string[]) => void>();
    const next: string[] = [];

    const shown = await loadRoom(pending, async () => {
      pending.hold((room) => room.push("joined"));
      return next;
    }, undefined);

    expect(shown).toBe(next);
    expect(next).toEqual(["joined"]);
    expect(pending.hold(() => {})).toBe(false);
  });

  // A door that refuses the cat keeps it where it was: the room still showing goes on, with what it said meanwhile.
  it("goes back to the room it stays in when loading is refused, with what was held", async () => {
    const pending = new PendingEvents<(room: string[]) => void>();
    const here: string[] = [];

    const refused = loadRoom(pending, async () => {
      pending.hold((room) => room.push("moved"));
      throw new Error("Walk up to the door to go through it.");
    }, here);

    await expect(refused).rejects.toThrow("Walk up to the door to go through it.");
    expect(here).toEqual(["moved"]);
    expect(pending.hold(() => {})).toBe(false);
  });

  it("drops what was held when loading fails with no room to stay in", async () => {
    const pending = new PendingEvents<(room: string[]) => void>();
    let applied = false;

    const failed = loadRoom(pending, async () => {
      pending.hold(() => (applied = true));
      throw new Error("Cat Café is full.");
    }, undefined);

    await expect(failed).rejects.toThrow("Cat Café is full.");
    expect(applied).toBe(false);
    expect(pending.hold(() => {})).toBe(false);
  });
});

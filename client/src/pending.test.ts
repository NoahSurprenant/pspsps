import { describe, expect, it } from "vitest";
import { PendingEvents } from "./pending";
import { Room } from "./room";
import type { CatView, ChatView, RoomSnapshot } from "./types";

const tom: CatView = { playerId: "tom", displayName: "Tom", furColor: "Grey", x: 0, z: 4, pose: null };
const felix: CatView = { playerId: "felix", displayName: "Felix", furColor: "Tuxedo", x: 1, z: 1, pose: null };
const hello: ChatView = { playerId: "felix", text: "hello", at: "2026-11-17T12:00:00Z" };

function snapshot(cats: CatView[], chat: ChatView[] = []): RoomSnapshot {
  return { worldId: "tabby", roomId: "town-square", roomName: "Town Square", width: 24, depth: 16, cats, chat, rooms: [], pond: null, doors: [] };
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

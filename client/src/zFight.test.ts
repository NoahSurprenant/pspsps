import { describe, expect, it } from "vitest";
import drafts from "../../src/Pspsps.Application/Rooms/ContentDrafts.json?raw";
import { blocksOfParts, blocksOfTerraces, zFights, type BackdropBox, type Block } from "./zFight";
import type { TerraceView } from "./types";

const block = (name: string, colour: string, box: Block["box"]): Block => ({ name, colour, box });

describe("zFights", () => {
  it("finds two boxes whose tops lie in one plane and overlap, painted differently", () => {
    const found = zFights([block("a", "#111111", [0, 1, 0, 4, 2, 4]), block("b", "#222222", [0.5, 1.5, 0, 1, 1, 1])]);

    expect(found).toHaveLength(1);
    expect(found[0]).toContain("top face at 2.00");
  });

  it("ignores faces of one colour, faces that only touch at an edge, faces a hair apart in height, and faces turned opposite ways", () => {
    expect(zFights([block("a", "#111111", [0, 1, 0, 4, 2, 4]), block("b", "#111111", [0.5, 1.5, 0, 1, 1, 1])])).toEqual([]);
    expect(zFights([block("a", "#111111", [0, 1, 0, 4, 2, 4]), block("b", "#222222", [4, 1, 0, 4, 2, 4])])).toEqual([]);
    expect(zFights([block("a", "#111111", [0, 1, 0, 4, 2, 4]), block("b", "#222222", [0, 1.01, 0, 2, 2, 2])])).toEqual([]);
    expect(zFights([block("a", "#111111", [0, 1, 0, 4, 2, 4]), block("b", "#222222", [0, 3, 0, 4, 2, 4])])).toEqual([]);
  });

  it("reads backdrop boxes, leaving out balls and turned parts, and terrace steps, leaving out round ones", () => {
    const parts: BackdropBox[] = [
      { shape: "box", width: 2, height: 2, depth: 2, at: [0, 1, 0], colour: "#111111" },
      { shape: "box", width: 2, height: 2, depth: 2, at: [0, 1, 0], colour: "#222222", turn: [0, 0.5, 0] },
      { shape: "ball", diameter: 2, at: [0, 1, 0], colour: "#333333" },
    ];
    const round: TerraceView = { id: "r", x: 0, z: 0, width: 4, depth: 4, height: 0.4, round: true } as unknown as TerraceView;

    expect(blocksOfParts(parts)).toHaveLength(1);
    expect(blocksOfTerraces([round])).toEqual([]);
  });

  it("finds two terraces whose steps are level where they overlap", () => {
    const one = { id: "a", x: 0, z: 0, width: 4, depth: 4, height: 0.4 } as unknown as TerraceView;
    const other = { id: "b", x: 1, z: 0, width: 4, depth: 4, height: 0.4, colour: "#c4262e" } as unknown as TerraceView;

    expect(zFights(blocksOfTerraces([one, other])).length).toBeGreaterThan(0);
  });
});

type Draft = { rooms: { id: string; layout: { terraces?: TerraceView[] }; look?: { backdrop?: { parts: BackdropBox[] } } }[] };
const rooms = (JSON.parse(drafts) as Draft).rooms;

// Rooms not yet reworked carry the flicker they had; none may get more, and the Town Square has none.
const before: Record<string, number> = {
  "cove": 13,
  "dojo-courtyard": 2,
  "dojo": 16,
  "plaza": 8,
  "pizza-parlor": 1,
  "night-club": 1,
  "mine": 16,
  "mine-tunnels": 22,
  "book-room": 4,
  "ice-rink": 18,
  "pet-shop": 1,
  "fort": 2,
  "forest": 3,
};

describe("the draft rooms", () => {
  it.each(rooms.map((room) => [room.id, room] as const))("%s has no more z-fighting faces than before", (id, room) => {
    const found = zFights([...blocksOfParts(room.look?.backdrop?.parts ?? []), ...blocksOfTerraces(room.layout.terraces ?? [])]);

    expect(found.length, found.slice(0, 20).join("\n")).toBeLessThanOrEqual(before[id] ?? 0);
  });
});

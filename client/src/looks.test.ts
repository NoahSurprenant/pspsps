import { afterEach, describe, expect, it, vi } from "vitest";
import seed from "../../src/Pspsps.Application/Rooms/RoomLooks.json?raw";
import serverLook from "../../src/Pspsps.Domain/Rooms/RoomLook.cs?raw";
import { openLandscape } from "./doors";
import {
  backdropGenerators, floorPainters, groundPainters, lookOf, setDraftLook, setLooks, wallPainters, wildlifeKinds, type LooksData, type RoomLookData,
} from "./looks";
import type { Floorplan } from "./paint";

const plans = {
  "town-square": { width: 24, depth: 16, pond: { x: -7, z: -3, radius: 2.5 }, doors: [] },
  cafe: { width: 14, depth: 10, pond: null, doors: [] },
  home: { width: 12, depth: 9, pond: null, doors: [] },
  shop: { width: 13, depth: 9, pond: null, doors: [] },
  meadow: { width: 18, depth: 12, pond: null, doors: [] },
  stream: { width: 24, depth: 14, pond: { x: -3, z: -2, radius: 2.4 }, doors: [] },
} satisfies Record<string, Floorplan>;

describe("lookOf", () => {
  it("lets an overlay's named variant change a room's colours and nothing else", () => {
    const [plain, winter] = [lookOf("cafe"), lookOf("cafe", "winter")];

    expect(winter.sky).not.toBe(plain.sky);
    expect(winter.paintFloor).toBe(plain.paintFloor);
    expect(lookOf("cafe", "no-such-look")).toBe(plain);
  });

  it("gives every home one look of its own, indoors", () => {
    const [home, cafe] = [lookOf("home"), lookOf("cafe")];

    expect(home.paintFloor).toBeDefined();
    expect(home.paintWall).toBeDefined();
    expect(home.paintFloor).not.toBe(cafe.paintFloor);
    expect(home.backdrop(plans.home, 4).length).toBeGreaterThan(0);
  });

  it("gives Town Square and the café each their own look", () => {
    const [square, cafe] = [lookOf("town-square"), lookOf("cafe")];

    expect(square.paintFloor).toBeDefined();
    expect(cafe.paintFloor).toBeDefined();
    expect(square.paintFloor).not.toBe(cafe.paintFloor);
    expect(square.sky).not.toBe(cafe.sky);
  });

  it("gives the shop a look of its own, indoors", () => {
    const [shop, cafe, home] = [lookOf("shop"), lookOf("cafe"), lookOf("home")];

    expect(shop.paintFloor).toBeDefined();
    expect(shop.paintWall).toBeDefined();
    expect(shop.paintFloor).not.toBe(cafe.paintFloor);
    expect(shop.paintFloor).not.toBe(home.paintFloor);
    expect(shop.ground).toBeUndefined();
  });

  it("gives a room it has no look for a plain floor and wall, and no backdrop", () => {
    const look = lookOf("attic");

    expect(look.paintFloor).toBeUndefined();
    expect(look.backdrop({ width: 14, depth: 10, pond: null, doors: [] }, 4)).toEqual([]);
  });

  it("gives rooms with different painters distinct outdoor looks", () => {
    const outdoors: RoomLookData = {
      sky: "#d1eaf3", floorColour: "#9fd38a", wallColour: "#f0ddba", ground: { colour: "#b7cf98" }, openLandscape: true,
      backdrop: { parts: [{ shape: "box", width: 1, height: 1, depth: 1, at: [0, 3, 8], colour: "#ffffff" }] },
    };
    setLooks({
      rooms: [
        { id: "meadow", look: { ...outdoors, floor: { painter: "garden" }, wall: { painter: "garden" }, backdrop: { parts: [] } } },
        { id: "stream", look: { ...outdoors, floor: { painter: "riverside" }, wall: { painter: "riverside" } } },
      ],
      variants: [],
    });
    const [meadow, stream] = [lookOf("meadow"), lookOf("stream")];

    expect(meadow.ground).toBeDefined();
    expect(stream.ground).toBeDefined();
    expect(meadow.paintFloor).toBeDefined();
    expect(stream.paintFloor).toBeDefined();
    expect(meadow.paintFloor).not.toBe(stream.paintFloor);
    expect(meadow.backdrop(plans.meadow, 4)).not.toEqual(stream.backdrop(plans.stream, 4));
  });

  // Hidden in the back wall, a part costs drawing and shows nothing.
  it.each(Object.entries(plans))("builds nothing of %s's backdrop inside the back wall", (room, plan) => {
    for (const part of lookOf(room).backdrop(plan, 4)) {
      const [x, y, z] = part.at;
      const inWall = Math.abs(x) < plan.width / 2 && y < 4 && z > plan.depth / 2 && z < plan.depth / 2 + 0.3;
      expect(inWall, `${part.shape} at ${part.at.join(", ")}`).toBe(false);
    }
  });

  // The backdrop is scenery: nothing in it stands where cats walk, or hangs low enough over the floor to hide one.
  it.each(Object.entries(plans))("keeps %s's backdrop off the floor, or above the cats' heads", (room, plan) => {
    for (const part of lookOf(room).backdrop(plan, 4)) {
      if (part.shape === "model") {
        continue;
      }
      const [x, y, z] = part.at;
      const [halfX, halfZ] = part.shape === "box" ? [part.width / 2, part.depth / 2] : [part.diameter / 2, part.diameter / 2];
      const height = part.shape === "ball" ? (part.height ?? part.diameter) : part.height;
      const overFloor = Math.abs(x) - halfX < plan.width / 2 && Math.abs(z) - halfZ < plan.depth / 2;
      if (overFloor) {
        expect(y - height / 2, `${part.shape} at ${part.at.join(", ")}`).toBeGreaterThan(2);
      }
    }
  });
});

describe("looks as data", () => {
  const newRoom: RoomLookData = {
    sky: "#112233", floorColour: "#445566", wallColour: "#778899",
    floor: { painter: "planks", colour: "#aa8855" }, wall: { painter: "bricks", accent: "#123456" },
    backdrop: { parts: [{ shape: "box", width: 1, height: 1, depth: 1, at: [0, 3, 8], colour: "#ffffff" }] },
  };

  afterEach(() => setLooks(JSON.parse(seed) as LooksData));

  it("draws a room the client has never heard of, from its painters' names and its own colours and parts", () => {
    setLooks({ rooms: [{ id: "loft", look: newRoom }], variants: [] });

    const look = lookOf("loft");

    expect(look.paintFloor).toBeDefined();
    expect(look.paintWall).toBeDefined();
    expect(look.floorNeedsProps).toBe(false);
    expect(look.backdrop({ width: 10, depth: 8, pond: null, doors: [] }, 4)).toEqual(newRoom.backdrop.parts);
  });

  it("lets a variant carry a floor of its own, kept apart from the room's own floor", () => {
    setLooks({
      rooms: [{ id: "loft", look: newRoom }],
      variants: [{ id: "frozen", sky: "#d8ecf7", wallColour: "#e8f0f5", floor: { painter: "tiles", colour: "#cfe9f6" } }, { id: "dusk", sky: "#223355", wallColour: "#445566" }],
    });

    const [plain, frozen, dusk] = [lookOf("loft"), lookOf("loft", "frozen"), lookOf("loft", "dusk")];

    expect(frozen.paintFloor).toBeDefined();
    expect(frozen.paintFloor).not.toBe(plain.paintFloor);
    expect(frozen.floorVariant).toBe("frozen");
    expect(dusk.paintFloor).toBe(plain.paintFloor);
    expect(plain.floorVariant).toBeUndefined();
    expect(dusk.floorVariant).toBeUndefined();
  });

  it("keeps the room's floor, and says so, when a variant's floor painter is unknown", () => {
    const warn = vi.spyOn(console, "warn").mockImplementation(() => {});
    setLooks({ rooms: [{ id: "loft", look: newRoom }], variants: [{ id: "frozen", sky: "#d8ecf7", wallColour: "#e8f0f5", floor: { painter: "marble" } }] });

    const frozen = lookOf("loft", "frozen");

    expect(frozen.paintFloor).toBe(lookOf("loft").paintFloor);
    expect(frozen.floorVariant).toBeUndefined();
    expect(warn).toHaveBeenCalledOnce();
    warn.mockRestore();
  });

  it("ignores a painter it doesn't know, painting the plain floor instead of failing", () => {
    setLooks({ rooms: [{ id: "loft", look: { ...newRoom, floor: { painter: "marble" } } }], variants: [] });

    expect(lookOf("loft").paintFloor).toBeUndefined();
  });

  it("shows a draft's look in place of the live one, with a new paint key each time so pictures are redrawn", () => {
    setLooks({ rooms: [{ id: "loft", look: newRoom }], variants: [] });
    const live = lookOf("loft");

    setDraftLook("loft", { ...newRoom, sky: "#abcdef", openLandscape: true });
    const first = lookOf("loft");
    setDraftLook("loft", { ...newRoom, sky: "#fedcba" });
    const second = lookOf("loft");

    expect(live.paintKey).toBeUndefined();
    expect(first.sky).toBe("#abcdef");
    expect(second.sky).toBe("#fedcba");
    expect(first.paintKey).toBeDefined();
    expect(second.paintKey).not.toBe(first.paintKey);
    expect(openLandscape("loft")).toBe(false);
  });

  it("knows which rooms are open countryside, from their looks", () => {
    expect(openLandscape("cafe")).toBe(false);
    setLooks({ rooms: [{ id: "loft", look: { ...newRoom, openLandscape: true } }], variants: [] });
    expect(openLandscape("loft")).toBe(true);
    expect(openLandscape("cafe")).toBe(false);
  });

  it("runs the town's generator beside any parts the look gives", () => {
    const square = lookOf("town-square").backdrop({ width: 24, depth: 16, pond: null, doors: [] }, 4);

    expect(square.length).toBeGreaterThan(0);
  });

  it("gives each room the wildlife its look names", () => {
    setLooks({
      rooms: [{ id: "stream", look: { ...newRoom, wildlife: "fish" } }, { id: "meadow", look: { ...newRoom, wildlife: "butterfly" } }],
      variants: [],
    });
    expect(lookOf("stream").wildlife).toBe("fish");
    expect(lookOf("meadow").wildlife).toBe("butterfly");
    expect(lookOf("cafe").wildlife).toBeUndefined();
  });

  it("names the same painters, generators and wildlife as the server checks a look against", () => {
    const listed = (field: string) =>
      [...(new RegExp(`${field} =\\s*\\[([^\\]]*)\\]`).exec(serverLook)?.[1] ?? "").matchAll(/"([^"]+)"/g)].map((m) => m[1]);

    expect(listed("Floors").sort()).toEqual(Object.keys(floorPainters).sort());
    expect(listed("Walls").sort()).toEqual(Object.keys(wallPainters).sort());
    expect(listed("Grounds").sort()).toEqual(Object.keys(groundPainters).sort());
    expect(listed("Generators").sort()).toEqual(Object.keys(backdropGenerators).sort());
    expect(listed("WildlifeKinds").sort()).toEqual([...wildlifeKinds].sort());
  });
});

import { describe, expect, it } from "vitest";
import {
  floorPixel, isGreen, largestTexture, paintCafeFloor, paintCafeWall, paintHomeFloor, paintHomeWall, paints, paintShopFloor, paintShopWall, paintStreet, paintTownSquareFloor, paintTownSquareWall, paintTurf,
  seeded, shaded, textureSize, type Brush, type Floorplan,
} from "./paint";
import { palette, topOf } from "./props";
import { lookOf } from "./looks";
import { doorMatColour } from "./scene";

const square: Floorplan = {
  width: 24, depth: 16, pond: { x: -7, z: -3, radius: 2.5 },
  doors: [{ id: "cafe", x: 7, z: 7, radius: 0.8, toRoomId: "cafe", toRoomName: "Cat Café" }],
};

/** A brush that draws nothing and remembers every call and every colour it was given. */
interface Call { name: string; args: unknown[]; fillStyle: unknown }

function recordingBrush(): { brush: Brush; calls: Call[]; colours: Set<string> } {
  const calls: Call[] = [];
  const colours = new Set<string>();
  const state: Record<string | symbol, unknown> = {};
  const brush = new Proxy(state, {
    get: (target, key) => key in target ? target[key] : (...args: unknown[]) => {
      calls.push({ name: String(key), args, fillStyle: target.fillStyle });
      return { width: 0 };
    },
    set: (target, key, value) => {
      if (key === "fillStyle" || key === "strokeStyle") colours.add(String(value).toLowerCase());
      target[key] = value;
      return true;
    },
  }) as unknown as Brush;
  return { brush, calls, colours };
}

const hex = (colour: string) => [1, 3, 5].map((i) => Number.parseInt(colour.slice(i, i + 2), 16));
/** The end-to-end tests' measure (e2e/tests/game.ts, pixelsNear): how far apart two colours are. */
const distance = (a: string, b: string) => hex(a).reduce((sum, value, i) => sum + Math.abs(value - (hex(b)[i] ?? 0)), 0);

describe("textureSize", () => {
  it("paints a metre the same number of pixels each way", () => {
    const size = textureSize(24, 16, 64);

    expect(size).toEqual({ width: 1536, height: 1024, scale: 64 });
  });

  it("paints a big surface coarser, never wider than the largest texture", () => {
    const size = textureSize(60, 4, 80);

    expect(size.width).toBe(largestTexture);
    expect(size.scale).toBeCloseTo(largestTexture / 60);
    expect(size.height).toBe(Math.round(4 * size.scale));
  });
});

describe("floorPixel", () => {
  // The texture's top is the back of the room, its left the room's left, as the camera sees it.
  it("puts the floor's back left corner at the top left, and its front right at the bottom right", () => {
    expect(floorPixel(square, 10, -12, 8)).toEqual([0, 0]);
    expect(floorPixel(square, 10, 12, -8)).toEqual([240, 160]);
    expect(floorPixel(square, 10, 0, 0)).toEqual([120, 80]);
  });
});

describe("seeded", () => {
  it("gives the same numbers for the same seed, all from 0 up to 1", () => {
    const [a, b] = [seeded(16), seeded(16)];
    const numbers = Array.from({ length: 1000 }, () => a());

    expect(numbers).toEqual(Array.from({ length: 1000 }, () => b()));
    expect(numbers.every((n) => n >= 0 && n < 1)).toBe(true);
    expect(new Set(numbers).size).toBeGreaterThan(990);
  });
});

describe("the painters", () => {
  // The pond's water and its edge are drawn over the floor, in the scene: its sand must be painted where they are.
  it("paints Town Square's sand round the pond, centred where the pond is drawn", () => {
    const { brush, calls } = recordingBrush();
    paintTownSquareFloor(brush, square, 10, { x: 0, z: 3.4 });

    const [x, y] = floorPixel(square, 10, -7, -3);
    const circles = calls.filter((c) => c.name === "arc" && c.args[0] === x && c.args[1] === y);
    expect(circles.map((c) => c.args[2])).toContain((2.5 + 0.6) * 10);
  });

  it("covers the full street wall before painting the individual shopfronts", () => {
    const { brush, calls } = recordingBrush();
    paintTownSquareWall(brush, 24, 4, 10, square.doors);
    expect(calls.find(call => call.name === "fillRect")?.args).toEqual([0, 0, 240, 40]);
  });

  // A shop window's awning reaches 0.95 m either side of its middle, and a door's awning 1.2 m either side of the door's,
  // at the same height: a window nearer a door than both together would run into it.
  it("paints no shop window into a door's awning", () => {
    // The pink house's second window is at x = 1.125: a door 2 m from it.
    const door = { id: "cafe", x: 3.125, z: 7, radius: 0.8, toRoomId: "cafe", toRoomName: "Cat Café" };
    const { brush, calls } = recordingBrush();
    paintTownSquareWall(brush, 24, 4, 10, [door]);

    const windows = calls.filter((c) => c.name === "fillRect" && c.fillStyle === paints.glass)
      .map((c) => (Number(c.args[0]) + Number(c.args[2]) / 2) / 10 - 12);
    expect(windows.length).toBeGreaterThan(4);
    for (const middle of windows) {
      expect(Math.abs(middle - door.x), `the window at ${middle}`).toBeGreaterThanOrEqual(0.95 + 1.2);
    }
  });

  // The café's wall is laid out from its width: a narrower café still has everything on its wall, and nothing off it.
  it.each([14, 9, 6])("paints the café's wall %s m wide with nothing off its edges", (width) => {
    const { brush, calls } = recordingBrush();
    paintCafeWall(brush, width, 4, 10, []);

    const rects = calls.filter((c) => c.name === "fillRect" || c.name === "strokeRect");
    expect(rects.length).toBeGreaterThan(10);
    for (const { args } of rects) {
      const [x, , w] = args.map(Number);
      expect(x, `${args.join(", ")}`).toBeGreaterThanOrEqual(0);
      expect((x ?? 0) + (w ?? 0), `${args.join(", ")}`).toBeLessThanOrEqual(width * 10 + 1e-9);
    }
  });

  // A home's wall is laid out round its door, by the back wall on the left (src/Pspsps.Domain/Homes/Home.cs).
  it("paints a home's wall with nothing off its edges, and nothing over its doorway", () => {
    const door = { id: "out", x: -4.4, z: 3.7, radius: 0.8, toRoomId: "town-square", toRoomName: "Town Square" };
    const { brush, calls } = recordingBrush();
    paintHomeWall(brush, 12, 4, 10, [door]);

    const framed = calls.filter((c) => c.name === "strokeRect");
    expect(framed.length).toBeGreaterThan(2);
    for (const { args } of calls.filter((c) => c.name === "fillRect" || c.name === "strokeRect")) {
      const [x, , w] = args.map(Number);
      expect(x, `${args.join(", ")}`).toBeGreaterThanOrEqual(0);
      expect((x ?? 0) + (w ?? 0), `${args.join(", ")}`).toBeLessThanOrEqual(120 + 1e-9);
    }
    for (const { args } of framed) {
      const [x, , w] = args.map(Number);
      const [from, to] = [(x ?? 0) / 10 - 6, ((x ?? 0) + (w ?? 0)) / 10 - 6];
      expect(to <= door.x - 1 || from >= door.x + 1, `${from} to ${to}`).toBe(true);
    }
  });

  // The rug is the catalogue's: centred under the stand (src/Pspsps.Domain/Rooms/RoomCatalog.cs).
  it("paints the shop's rug under the catalogue stand", () => {
    const plan = { ...square, width: 13, depth: 9, pond: null, doors: [] };
    const { brush, calls } = recordingBrush();
    paintShopFloor(brush, plan, 10);

    const [x, y] = floorPixel(plan, 10, 1.4, -0.6);
    const rug = calls.filter((c) => c.name === "ellipse");
    expect(rug.length).toBeGreaterThan(0);
    for (const { args } of rug) {
      expect([args[0], args[1]]).toEqual([x, y]);
    }
  });

  // The shop's wall is laid out round its door, by the back wall on the left (src/Pspsps.Domain/Rooms/RoomCatalog.cs).
  it("paints the shop's wall with nothing off its edges, and nothing framed over its doorway", () => {
    const door = { id: "town-square", x: -4.9, z: 3.6, radius: 0.8, toRoomId: "town-square", toRoomName: "Town Square" };
    const { brush, calls } = recordingBrush();
    paintShopWall(brush, 13, 4, 10, [door]);

    const framed = calls.filter((c) => c.name === "strokeRect" && Number(c.args[2]) > 8);
    expect(framed.length).toBeGreaterThanOrEqual(3);
    for (const { args } of calls.filter((c) => c.name === "fillRect" || c.name === "strokeRect")) {
      const [x, , w] = args.map(Number);
      expect(x, `${args.join(", ")}`).toBeGreaterThanOrEqual(0);
      expect((x ?? 0) + (w ?? 0), `${args.join(", ")}`).toBeLessThanOrEqual(130 + 1e-9);
    }
    for (const { args } of framed.filter((c) => Number(c.args[1]) < 25)) {
      const [x, , w] = args.map(Number);
      const [from, to] = [(x ?? 0) / 10 - 6.5, ((x ?? 0) + (w ?? 0)) / 10 - 6.5];
      expect(to <= door.x - 1 || from >= door.x + 1, `${from} to ${to}`).toBe(true);
    }
  });

  it("paints the same floor every time", () => {
    const [first, second] = [recordingBrush(), recordingBrush()];
    paintTownSquareFloor(first.brush, square, 10, { x: 0, z: 3.4 });
    paintTownSquareFloor(second.brush, square, 10, { x: 0, z: 3.4 });

    expect(first.calls).toEqual(second.calls);
  });

  // The end-to-end layout test finds the pond and the door mats by their colours, and the cats' eyes by theirs: nothing
  // painted or built in a room may come near them.
  it("never paints or builds in the pond's blue, a door mat's red or a cat's eye yellow", () => {
    const used = new Set<string>([...Object.values(palette), ...Object.values(paints)]);
    for (const paint of [
      (b: Brush) => paintTownSquareFloor(b, square, 10, { x: 0, z: 3.4 }),
      (b: Brush) => paintTownSquareWall(b, 24, 4, 10, square.doors),
      (b: Brush) => paintCafeFloor(b, { ...square, width: 14, depth: 10, pond: null }, 10),
      (b: Brush) => paintCafeWall(b, 14, 4, 10, []),
      (b: Brush) => paintStreet(b, 10),
      (b: Brush) => paintHomeFloor(b, { ...square, width: 12, depth: 9, pond: null }, 10),
      (b: Brush) => paintHomeWall(b, 12, 4, 10, []),
      (b: Brush) => paintShopFloor(b, { ...square, width: 13, depth: 9, pond: null }, 10),
      (b: Brush) => paintShopWall(b, 13, 4, 10, []),
    ]) {
      const { brush, colours } = recordingBrush();
      paint(brush);
      colours.forEach((colour) => used.add(colour));
    }
    for (const room of ["town-square", "cafe", "home", "shop", "elsewhere"]) {
      const look = lookOf(room);
      [look.sky, look.floorColour, look.wallColour, look.ground?.colour].forEach((colour) => colour && used.add(colour));
      look.backdrop(square, 4).forEach((part) => used.add(part.colour));
    }

    // A built part's top is drawn a little lighter than its sides: those colours count too.
    for (const colour of [...used]) used.add(topOf(colour));

    expect(used.size).toBeGreaterThan(30);
    for (const colour of used) {
      expect(colour, colour).toMatch(/^#[0-9a-f]{6}$/i);
      for (const [name, kept] of [["the pond's blue", "#5fb3d9"], ["a door mat's red", doorMatColour], ["a cat's eye yellow", "#ffc85a"]] as const) {
        expect(distance(colour, kept), `${colour} is near ${name}`).toBeGreaterThanOrEqual(60);
      }
    }
  });
});

describe("paintTurf", () => {
  it("paints the same turf every time, in several greens, with blades and flowers, and tiles it", () => {
    const first = recordingBrush();
    const again = recordingBrush();
    paintTurf(first.brush, 8, 20, "#93c46f", seeded(7));
    paintTurf(again.brush, 8, 20, "#93c46f", seeded(7));

    expect(first.calls).toEqual(again.calls);
    expect(first.colours.size).toBeGreaterThan(6);
    expect([...first.colours].filter((colour) => isGreen(colour)).length).toBeGreaterThan(4);
    // Each tuft is drawn on the nine tiles round it, so an edge matches the one it meets.
    const strokes = first.calls.filter((call) => call.name === "stroke").length;
    expect(strokes).toBeGreaterThan(8 * 8 * 14 * 9 - 1);
  });

  it("gives a colour that is not green grain, not blades", () => {
    const sand = recordingBrush();
    const grass = recordingBrush();
    paintTurf(sand.brush, 8, 20, "#efe0b0", seeded(7));
    paintTurf(grass.brush, 8, 20, "#93c46f", seeded(7));

    expect(sand.calls.filter((call) => call.name === "stroke").length).toBeLessThan(grass.calls.filter((call) => call.name === "stroke").length);
  });

  it("shades a colour and knows a green", () => {
    expect(shaded("#808080", 0.5)).toBe("#404040");
    expect(shaded("#f0f0f0", 2)).toBe("#ffffff");
    expect([isGreen("#93c46f"), isGreen("#efe0b0"), isGreen("#cc4a3a")]).toEqual([true, false, false]);
  });
});

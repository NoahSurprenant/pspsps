import { describe, expect, it } from "vitest";
import { canRead, catalogueStand, goRead, readingSpot } from "./catalogue";
import { DoorWalks } from "./doors";
import type { PropView } from "./types";

const stand: PropView = { kind: "catalogue-stand", x: 1.4, z: -0.6, radius: 0.6, facing: 0 };

describe("catalogueStand", () => {
  it("finds the shop's stand among a room's props, and none in a room without it", () => {
    const plant: PropView = { kind: "plant", x: 0, z: 0, radius: 0.5, facing: 0 };

    expect(catalogueStand([plant, stand])).toBe(stand);
    expect(catalogueStand([plant])).toBeUndefined();
    expect(catalogueStand(null)).toBeUndefined();
  });
});

describe("readingSpot", () => {
  it("is just in front of the stand, off it, the way it faces", () => {
    const spot = readingSpot(stand);
    expect(spot.x).toBeCloseTo(1.4);
    expect(spot.z).toBeCloseTo(-0.6 - 1.05);
    expect(Math.hypot(spot.x - stand.x, spot.z - stand.z)).toBeGreaterThan(stand.radius);

    // Turned to face the left (-X), it is read from its left.
    const left = readingSpot({ ...stand, facing: Math.PI / 2 });
    expect(left.x).toBeCloseTo(1.4 - 1.05);
    expect(left.z).toBeCloseTo(-0.6);
  });
});

describe("canRead", () => {
  it("is near enough to read without walking up: from the reading spot, not from across the room", () => {
    expect(canRead(stand, readingSpot(stand))).toBe(true);
    expect(canRead(stand, { x: 2.8, z: 0.4 })).toBe(true);
    expect(canRead(stand, { x: -1.6, z: 1 })).toBe(false);
  });
});

describe("goRead", () => {
  /** A cat that walks where it is sent: there at once, unless <arrives> says otherwise. */
  function reading(arrives: Promise<boolean> = Promise.resolve(true)) {
    const heard: string[] = [];
    const walks = new DoorWalks();
    return {
      heard,
      walks,
      how: {
        walks,
        move: (x: number, z: number) => {
          heard.push(`move ${x.toFixed(2)} ${z.toFixed(2)}`);
          return Promise.resolve();
        },
        reached: () => arrives,
        open: () => {
          heard.push("open");
          return Promise.resolve();
        },
      },
    };
  }

  it("opens the catalogue at once for a cat near the stand", async () => {
    const { heard, how } = reading();

    await goRead(stand, { x: 2, z: -1.5 }, how);

    expect(heard).toEqual(["open"]);
  });

  it("walks a cat across the room up to the stand, and opens the catalogue once it is there", async () => {
    const { heard, how } = reading();

    await goRead(stand, { x: -1.6, z: 1 }, how);

    expect(heard).toEqual(["move 1.40 -1.65", "open"]);
  });

  it("opens nothing when the cat is sent elsewhere on the way", async () => {
    let arrive: (there: boolean) => void = () => {};
    const { heard, how, walks } = reading(new Promise((resolve) => (arrive = resolve)));

    const going = goRead(stand, { x: -1.6, z: 1 }, how);
    // A tap on the floor meanwhile: another walk.
    walks.callOff();
    arrive(true);
    await going;

    expect(heard).toEqual(["move 1.40 -1.65"]);
  });

  it("opens nothing when the cat never gets there (another room showed)", async () => {
    const { heard, how } = reading(Promise.resolve(false));

    await goRead(stand, { x: -1.6, z: 1 }, how);

    expect(heard).toEqual(["move 1.40 -1.65"]);
  });
});

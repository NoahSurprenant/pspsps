import { describe, expect, it } from "vitest";
import { FILTERS, VIEW, filterLabel, isOpen, matches, placeName, screenPoint, shouldTravel } from "./islandMap";
import type { MapPlaceView } from "./types";

const place = (over: Partial<MapPlaceView> = {}): MapPlaceView => ({
  id: "cove", name: "Sunny Cove", category: "games", landmark: "cove", x: 0, z: 0, blurb: "", roomId: "cove", ...over,
});

describe("the island map", () => {
  it("keeps every place for All and only a category's own places for a tab", () => {
    expect(FILTERS.map(filterLabel)).toEqual(["All", "Places", "Games", "Shops", "Pets"]);
    expect(matches(place(), "all")).toBe(true);
    expect(matches(place(), "games")).toBe(true);
    expect(matches(place(), "shops")).toBe(false);
  });

  it("puts the island's centre in the middle of the picture and north higher up", () => {
    expect(screenPoint(0, 0)).toEqual({ left: 50, top: 50 });
    expect(screenPoint(0, 20).top).toBeLessThan(50);
    expect(screenPoint(-20, 0).left).toBeLessThan(50);
    expect(screenPoint(0, 0, 4).top).toBeLessThan(50);
  });

  it("fits the whole island, shore to shore, inside the picture", () => {
    for (const [x, z] of [[46, 0], [-46, 0], [0, 42], [0, -42]] as const) {
      const { left, top } = screenPoint(x, z);
      expect(left).toBeGreaterThan(0);
      expect(left).toBeLessThan(100);
      expect(top).toBeGreaterThan(0);
      expect(top).toBeLessThan(100);
    }
    expect(VIEW.halfWidth / VIEW.halfHeight).toBeCloseTo(4 / 3);
  });

  it("names a place for a screen reader by where you stand and whether you can go", () => {
    expect(placeName(place(), "town-square")).toBe("Sunny Cove");
    expect(placeName(place(), "cove")).toBe("Sunny Cove, you are here");
    expect(placeName(place({ roomId: null }), "cove")).toBe("Sunny Cove, coming soon");
    expect(isOpen(place({ roomId: null }))).toBe(false);
  });

  it("lets a touch select first and a second touch go, while a mouse or the keyboard goes at once", () => {
    expect(shouldTravel(false, "touch")).toBe(false);
    expect(shouldTravel(true, "touch")).toBe(true);
    expect(shouldTravel(false, "mouse")).toBe(true);
    expect(shouldTravel(false, "")).toBe(true);
  });
});

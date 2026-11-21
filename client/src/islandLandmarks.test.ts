import { describe, expect, it } from "vitest";
import mapSource from "../../src/Pspsps.Domain/Rooms/IslandMap.json?raw";
import scene from "./islandScene.ts?raw";

const places = JSON.parse(mapSource) as { id: string; landmark: string }[];

describe("island landmarks", () => {
  it("has a drawer for every landmark named in the map", () => {
    const landmarks = [...new Set(places.map((p) => p.landmark))];
    expect(landmarks.length).toBeGreaterThan(0);
    for (const landmark of landmarks) {
      expect(scene, landmark).toMatch(new RegExp(`^\\s+${landmark}: \\(p\\) => `, "m"));
    }
  });
});

import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";

const places = JSON.parse(readFileSync("../src/Pspsps.Domain/Rooms/IslandMap.json", "utf8")) as {
  id: string;
  landmark: string;
}[];
const scene = readFileSync("src/islandScene.ts", "utf8");

describe("island landmarks", () => {
  it("has a drawer for every landmark named in the map", () => {
    const landmarks = [...new Set(places.map((p) => p.landmark))];
    expect(landmarks.length).toBeGreaterThan(0);
    for (const landmark of landmarks) {
      expect(scene, landmark).toMatch(new RegExp(`^\\s+${landmark}: \\(p\\) => `, "m"));
    }
  });
});

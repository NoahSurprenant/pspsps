import { describe, expect, it } from "vitest";
import { RoomActivities } from "./roomActivities";
import { propParts } from "./props";

const tree = { kind: "pear-tree", x: 1, z: 1, radius: 1.4, facing: 0 };
const crate = { ...tree, kind: "packing-crate" };
const table = { ...tree, kind: "potting-bench" };

describe("room activities", () => {
  it("draws ripe trees, carries one pear at a time and ships a complete crate", () => {
    const visit = new RoomActivities();
    const trees = [tree, { ...tree, x: 5 }];
    const ripe = propParts(tree, visit.stage(tree));
    for (const fruit of trees) {
      visit.tend(fruit, trees.length);
      expect(propParts(fruit, visit.stage(fruit)).length).toBeLessThan(ripe.length);
      expect(visit.tend(tree, trees.length)).toContain("crate first");
      visit.tend(crate, trees.length);
      expect(visit.tend(fruit, trees.length)).toContain("picked");
    }
    expect(visit.stage(crate)).toBe(2);
    expect(propParts(crate, 2).length).toBeGreaterThan(propParts(crate, 0).length);
    expect(visit.tend(crate, 2)).toContain("shipped");
    expect(visit.stage(crate)).toBe(0);
    expect(trees.map(fruit => visit.stage(fruit))).toEqual([0, 0]);
  });

  it("opens one more flower per watering and replants the pots after three", () => {
    const visit = new RoomActivities();
    for (const stage of [1, 2, 3, 0]) {
      visit.tend(table, 0);
      expect(visit.stage(table)).toBe(stage);
      if (stage) expect(propParts(table, stage).length).toBeGreaterThan(propParts(table, stage - 1).length);
    }
  });
});

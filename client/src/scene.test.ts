import { NullEngine, Scene, TransformNode } from "@babylonjs/core";
import { CreateBox } from "@babylonjs/core/Meshes/Builders/boxBuilder";
import { describe, expect, it } from "vitest";
import { catPlayerIdOf, RoomScene } from "./scene";

describe("RoomScene", () => {
  // The page draws from the moment it loads, before any room is showing (sign-in, world picker). A frame that throws
  // stops Babylon's render loop for good, so no room would ever draw after it.
  it("draws a frame before any room is showing", () => {
    const scene = new RoomScene(new NullEngine());

    expect(() => scene.renderFrame()).not.toThrow();
  });
});

describe("catPlayerIdOf", () => {
  it("finds the cat a picked mesh is part of, however deep, and nothing for other meshes", () => {
    const scene = new Scene(new NullEngine());
    const root = new TransformNode("cat", scene);
    root.metadata = { catPlayerId: "felix" };
    const body = new TransformNode("body", scene);
    body.parent = root;
    const ear = CreateBox("ear", {}, scene);
    ear.parent = body;

    expect(catPlayerIdOf(ear)).toBe("felix");
    expect(catPlayerIdOf(CreateBox("floor", {}, scene))).toBeUndefined();
    expect(catPlayerIdOf(null)).toBeUndefined();
  });
});

describe("RoomScene's cats", () => {
  // Other cats take the pointer (their cards); the player's own doesn't, so a tap just behind it still walks or fishes.
  it("makes other cats pickable, and not the player's own", () => {
    const view = new RoomScene(new NullEngine());
    view.playerId = "tom";
    view.place({ playerId: "tom", displayName: "Tom", furColor: "Grey", x: 0, z: 0, pose: null });
    view.place({ playerId: "felix", displayName: "Felix", furColor: "Black", x: 2, z: 0, pose: null });

    const meshes = (view as unknown as { scene: Scene }).scene.meshes;
    const pickable = (id: string) => [...new Set(meshes.filter((m) => catPlayerIdOf(m) === id && !m.name.startsWith("anchor-")).map((m) => m.isPickable))];

    expect(pickable("felix")).toEqual([true]);
    expect(pickable("tom")).toEqual([false]);
  });
});

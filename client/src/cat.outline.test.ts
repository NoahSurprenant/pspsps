// Only the client's own modules and Babylon by subpath, as the shipped page loads them (no import of Babylon's index,
// which registers every side effect): a cat's outlines are drawn only if something the cat loads registers the outline
// renderer, which renderOutline otherwise silently does nothing without.
import { NullEngine } from "@babylonjs/core/Engines/nullEngine";
import { TransformNode } from "@babylonjs/core/Meshes/transformNode";
import { Scene } from "@babylonjs/core/scene";
import { describe, expect, it } from "vitest";
import { Cat, loadCatModel } from "./cat";
import { modelBytes } from "./testCatModel";

describe("a cat's outlines", () => {
  it("are drawn: the cat brings the outline renderer with it", async () => {
    const scene = new Scene(new NullEngine());
    const model = await loadCatModel(scene, modelBytes());

    new Cat(scene, model, new TransformNode("cat", scene), "cat-a", "Grey");

    expect(scene.meshes.some((mesh) => mesh.renderOutline)).toBe(true);
    expect(scene._getComponent("Outline")?.constructor.name).toBe("OutlineRenderer");
  });
});

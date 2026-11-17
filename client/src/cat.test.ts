import { NullEngine } from "@babylonjs/core/Engines/nullEngine";
import type { StandardMaterial } from "@babylonjs/core/Materials/standardMaterial";
import { Matrix, Vector3 } from "@babylonjs/core/Maths/math.vector";
import { TransformNode } from "@babylonjs/core/Meshes/transformNode";
import { Scene } from "@babylonjs/core/scene";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { animationFor, Cat, catAnimations, colourOf, colourOfMesh, facing, loadCatModel, loops, type CatDoing } from "./cat";
import { coatOf } from "./fur";
import catGlb from "./models/cat.glb?inline";

/** The model's bytes, from the data URL Vite makes of it. */
function modelBytes(): Uint8Array {
  return Uint8Array.from(atob(catGlb.slice(catGlb.indexOf(",") + 1)), (c) => c.charCodeAt(0));
}

const standing: CatDoing = { moving: false, pose: undefined, waving: false, meowing: false };

describe("animationFor", () => {
  it("idles when doing nothing, and walks while moving", () => {
    expect(animationFor(standing)).toBe("idle");
    expect(animationFor({ ...standing, moving: true })).toBe("walk");
  });

  it("holds a pose, and walking beats it (a walk ends the pose)", () => {
    expect(animationFor({ ...standing, pose: "sit" })).toBe("sit");
    expect(animationFor({ ...standing, pose: "sleep" })).toBe("sleep");
    expect(animationFor({ ...standing, pose: "sit", moving: true, waving: true, meowing: true })).toBe("walk");
  });

  it("waves for its moment, over a pose too", () => {
    expect(animationFor({ ...standing, waving: true })).toBe("wave");
    expect(animationFor({ ...standing, pose: "sleep", waving: true })).toBe("wave");
  });

  it("meows standing; sitting or asleep the pose holds (the bubble says it)", () => {
    expect(animationFor({ ...standing, meowing: true })).toBe("meow");
    expect(animationFor({ ...standing, pose: "sit", meowing: true })).toBe("sit");
    expect(animationFor({ ...standing, waving: true, meowing: true })).toBe("wave");
  });

  it("loops everything but the meow, which plays once", () => {
    expect(catAnimations.filter((animation) => !loops(animation))).toEqual(["meow"]);
  });
});

describe("facing", () => {
  // The model looks along +z: a turn of 0 faces +z, a quarter turn +x.
  it("turns the cat the way it walks", () => {
    expect(facing(0, 1)).toBeCloseTo(0);
    expect(facing(1, 0)).toBeCloseTo(Math.PI / 2);
    expect(facing(-1, 0)).toBeCloseTo(-Math.PI / 2);
    expect(Math.abs(facing(0, -1))).toBeCloseTo(Math.PI);
  });

  it("turns the model's +z onto the direction of travel", () => {
    for (const [dx, dz] of [[3, 4], [-2, 1], [0.5, -5]] as const) {
      // What rotation.y = facing(dx, dz) does to the model's +z.
      const turned = Vector3.TransformCoordinates(new Vector3(0, 0, 1), Matrix.RotationY(facing(dx, dz)));
      const length = Math.hypot(dx, dz);
      expect(turned.x).toBeCloseTo(dx / length);
      expect(turned.z).toBeCloseTo(dz / length);
    }
  });
});

describe("colourOf", () => {
  const tuxedo = coatOf("Tuxedo");

  it("paints the coat and accent from the fur's palette", () => {
    expect(colourOf("coat", tuxedo, false)).toBe(tuxedo.body);
    expect(colourOf("accent", tuxedo, false)).toBe(tuxedo.accent);
  });

  it("keeps the eyes, pupils and dark features whatever the fur", () => {
    for (const colour of ["eyes", "pupils", "dark"] as const) {
      expect(colourOf(colour, coatOf("Orange"), false)).toBe(colourOf(colour, coatOf("White"), false));
    }
  });

  it("shuts the eyes asleep: eyes and pupils take the coat's colour; the mouth stays", () => {
    expect(colourOf("eyes", tuxedo, true)).toBe(tuxedo.body);
    expect(colourOf("pupils", tuxedo, true)).toBe(tuxedo.body);
    expect(colourOf("dark", tuxedo, true)).toBe(colourOf("dark", tuxedo, false));
  });
});

describe("colourOfMesh", () => {
  it("reads the colour from the end of a mesh's name, cloned names included", () => {
    expect(colourOfMesh("body.coat")).toBe("coat");
    expect(colourOfMesh("cat-1-leg-front-left.accent")).toBe("accent");
    expect(colourOfMesh("cat-1-body.pupils")).toBe("pupils");
    expect(colourOfMesh("cat-1-__root__")).toBeUndefined();
    expect(colourOfMesh("body.fur")).toBeUndefined();
  });
});

// The real model (src/models/cat.glb), read the way the game reads it, without a GPU.
describe("Cat", () => {
  let engine: NullEngine;
  let scene: Scene;

  beforeEach(() => {
    engine = new NullEngine();
    scene = new Scene(engine);
  });

  afterEach(() => {
    scene.dispose();
    engine.dispose();
  });

  async function makeCat(name: string, fur: Parameters<typeof coatOf>[0]): Promise<{ cat: Cat; root: TransformNode }> {
    const model = await loadCatModel(scene, modelBytes());
    const root = new TransformNode(name, scene);
    return { cat: new Cat(scene, model, root, name, fur), root };
  }

  function materialOf(root: TransformNode, meshName: string): StandardMaterial {
    const mesh = root.getChildMeshes(false).find((child) => child.name === meshName);
    expect(mesh, meshName).toBeDefined();
    return mesh?.material as StandardMaterial;
  }

  it("has every clip, and idles to start with", async () => {
    const { cat } = await makeCat("cat-a", "Grey");

    expect(cat.animation).toBe("idle");
    const names = scene.animationGroups.map((group) => group.name);
    for (const animation of catAnimations) {
      expect(names).toContain(`cat-a-${animation}`);
    }
    expect(scene.animationGroups.filter((group) => group.isPlaying).map((group) => group.name)).toEqual(["cat-a-idle"]);
  });

  it("plays one clip at a time", async () => {
    const { cat } = await makeCat("cat-a", "Grey");

    cat.play("walk");

    expect(cat.animation).toBe("walk");
    expect(scene.animationGroups.filter((group) => group.isPlaying).map((group) => group.name)).toEqual(["cat-a-walk"]);
  });

  it("paints the fur on the model, outlined, and shuts the eyes asleep", async () => {
    const { cat, root } = await makeCat("cat-a", "Calico");
    const calico = coatOf("Calico");

    expect(materialOf(root, "cat-a-body.coat").emissiveColor.toHexString().toLowerCase()).toBe(calico.body);
    expect(materialOf(root, "cat-a-tail.accent").emissiveColor.toHexString().toLowerCase()).toBe(calico.accent);
    expect(root.getChildMeshes(false).find((mesh) => mesh.name === "cat-a-body.coat")?.renderOutline).toBe(true);
    const eyes = materialOf(root, "cat-a-body.eyes");
    expect(eyes.emissiveColor.toHexString().toLowerCase()).not.toBe(calico.body);

    cat.play("sleep");
    expect(eyes.emissiveColor.toHexString().toLowerCase()).toBe(calico.body);
    cat.play("idle");
    expect(eyes.emissiveColor.toHexString().toLowerCase()).not.toBe(calico.body);
  });

  it("gives each cat its own colours", async () => {
    const { root: black } = await makeCat("cat-a", "Black");
    const { root: white } = await makeCat("cat-b", "White");

    expect(materialOf(black, "cat-a-body.coat").emissiveColor.toHexString().toLowerCase()).toBe(coatOf("Black").body);
    expect(materialOf(white, "cat-b-body.coat").emissiveColor.toHexString().toLowerCase()).toBe(coatOf("White").body);
  });

  it("has a head-top and a neck to hang hats and collars on, which follow the body", async () => {
    const { cat, root } = await makeCat("cat-a", "Grey");
    const { headTop, neck } = cat.anchors;

    expect(headTop.name).toBe("cat-a-head-top");
    expect(neck.name).toBe("cat-a-neck");
    expect(headTop.parent?.name).toBe("cat-a-body");
    expect(neck.parent?.name).toBe("cat-a-body");
    root.computeWorldMatrix(true);
    const top = headTop.getAbsolutePosition();
    const collar = neck.getAbsolutePosition();
    // The head's top is above the neck, and the neck is at the front (the model looks along +z).
    expect(top.y).toBeGreaterThan(collar.y);
    expect(top.y).toBeGreaterThan(0.7);
    expect(collar.z).toBeGreaterThan(0.3);
  });

  it("leaves nothing behind when its root goes", async () => {
    const { root } = await makeCat("cat-a", "Grey");

    root.dispose();

    expect(scene.animationGroups).toEqual([]);
    expect(scene.meshes.filter((mesh) => mesh.name.startsWith("cat-a"))).toEqual([]);
    expect(scene.materials.filter((material) => material.name.startsWith("cat-a"))).toEqual([]);
  });
});

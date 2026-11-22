import { NullEngine } from "@babylonjs/core/Engines/nullEngine";
import type { StandardMaterial } from "@babylonjs/core/Materials/standardMaterial";
import { Matrix, Vector3 } from "@babylonjs/core/Maths/math.vector";
import { TransformNode } from "@babylonjs/core/Meshes/transformNode";
import { Scene } from "@babylonjs/core/scene";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { animationFor, Cat, catAnimations, colourOf, colourOfMesh, facing, loadCatModel, loops, MeowClip, type CatDoing } from "./cat";
import { coatOf } from "./fur";
import { modelBytes } from "./testCatModel";

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

describe("MeowClip", () => {
  it("shows a meow that comes while the cat stands idle, from the start, for as long as it lasts", () => {
    const clip = new MeowClip();
    expect(clip.next(0, false, "idle")).toEqual({ show: false, restart: false });

    expect(clip.next(1, true, "idle")).toEqual({ show: true, restart: true });
    expect(clip.next(1, true, "idle")).toEqual({ show: true, restart: false });
    // Another meow starts it again.
    expect(clip.next(2, true, "idle")).toEqual({ show: true, restart: true });
    expect(clip.next(2, false, "idle")).toEqual({ show: false, restart: false });
  });

  // Heard (its bubble), not shown: the head doesn't lift a second later, when the meow is long over.
  it("doesn't show a meow that came while the cat walked, waved or held a pose, even once it's idle again", () => {
    for (const busy of ["walk", "wave", "sit"] as const) {
      const clip = new MeowClip();
      expect(clip.next(1, true, busy)).toEqual({ show: false, restart: false });
      expect(clip.next(1, true, "idle")).toEqual({ show: false, restart: false });
    }
  });

  it("stops showing when the cat walks off mid-meow, and doesn't come back", () => {
    const clip = new MeowClip();
    clip.next(1, true, "idle");

    expect(clip.next(1, true, "walk").show).toBe(false);
    expect(clip.next(1, true, "idle").show).toBe(false);
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

  // The model the cats are cloned from stays out of sight: a clip playing on it would animate it every frame for nothing.
  it("reads the model with none of its clips playing", async () => {
    const model = await loadCatModel(scene, modelBytes());

    expect(model.animationGroups.map((group) => group.name).sort()).toEqual([...catAnimations].sort());
    expect(model.animationGroups.filter((group) => group.isPlaying)).toEqual([]);
  });

  it("refuses a model without a body, leaving nothing of it behind", async () => {
    const model = await loadCatModel(scene, modelBytes());
    for (const node of model.transformNodes.filter((node) => node.name === "body")) {
      node.name = "torso";
    }
    const root = new TransformNode("cat-a", scene);

    expect(() => new Cat(scene, model, root, "cat-a", "Grey")).toThrow(/no body/);
    expect(root.getChildren()).toEqual([]);
    expect(scene.animationGroups).toEqual([]);
    expect(scene.materials.filter((material) => material.name.startsWith("cat-a"))).toEqual([]);
  });

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

  it("wears one hat on its head-top and one collar at its neck, outlined, a new one in place of the old", async () => {
    const { cat } = await makeCat("cat-a", "Grey");

    cat.dress({ fur: "Grey", hat: "party-hat", collar: "bell-collar" });
    expect(cat.wearing).toEqual({ fur: "Grey", hat: "party-hat", collar: "bell-collar" });
    const hat = scene.getTransformNodeByName("cat-a-hat");
    expect(hat?.parent).toBe(cat.anchors.headTop);
    expect(scene.getTransformNodeByName("cat-a-collar")?.parent).toBe(cat.anchors.neck);
    expect(hat?.getChildMeshes(false).length).toBeGreaterThan(0);
    expect(hat?.getChildMeshes(false).every((mesh) => mesh.renderOutline)).toBe(true);

    cat.dress({ fur: "Grey", hat: "crown", collar: "bell-collar" });
    expect(cat.wearing.hat).toBe("crown");
    expect(scene.meshes.filter((mesh) => mesh.name.startsWith("cat-a-hat-cone"))).toEqual([]);
    expect(scene.materials.filter((material) => material.name.startsWith("cat-a-hat-cone"))).toEqual([]);
    expect(scene.meshes.filter((mesh) => mesh.name.startsWith("cat-a-hat-point"))).toHaveLength(5);
  });

  it("takes things off, and leaves off what it can't draw", async () => {
    const { cat } = await makeCat("cat-a", "Grey");
    cat.dress({ fur: "Grey", hat: "beanie", collar: "red-collar" });

    cat.dress({ fur: "Grey", hat: null, collar: "a-collar-from-the-future" });

    expect(cat.wearing).toEqual({ fur: "Grey", hat: undefined, collar: undefined });
    expect(scene.meshes.filter((mesh) => mesh.name.startsWith("cat-a-hat") || mesh.name.startsWith("cat-a-collar"))).toEqual([]);
  });

  it("changes its coat, and still shuts its eyes asleep in the new one", async () => {
    const { cat, root } = await makeCat("cat-a", "Grey");

    cat.dress({ fur: "Tuxedo", hat: null, collar: null });

    const tuxedo = coatOf("Tuxedo");
    expect(cat.wearing.fur).toBe("Tuxedo");
    expect(materialOf(root, "cat-a-body.coat").emissiveColor.toHexString().toLowerCase()).toBe(tuxedo.body);
    expect(materialOf(root, "cat-a-tail.accent").emissiveColor.toHexString().toLowerCase()).toBe(tuxedo.accent);
    cat.play("sleep");
    expect(materialOf(root, "cat-a-body.eyes").emissiveColor.toHexString().toLowerCase()).toBe(tuxedo.body);
  });

  it("leaves nothing behind when its root goes, what it wears included", async () => {
    const { cat, root } = await makeCat("cat-a", "Grey");
    cat.dress({ fur: "Grey", hat: "top-hat", collar: "green-collar" });

    root.dispose();

    expect(scene.animationGroups).toEqual([]);
    expect(scene.meshes.filter((mesh) => mesh.name.startsWith("cat-a"))).toEqual([]);
    expect(scene.materials.filter((material) => material.name.startsWith("cat-a"))).toEqual([]);
  });
});

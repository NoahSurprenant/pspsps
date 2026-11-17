// Babylon by module: the animation runtime (AnimationGroup.start) and the glTF 2.0 loader, for their side effects.
import "@babylonjs/core/Animations/animatable";
import "@babylonjs/loaders/glTF/2.0/glTFLoader";
import { GLTFLoaderAnimationStartMode } from "@babylonjs/loaders/glTF/glTFFileLoader.pure";
import type { AnimationGroup } from "@babylonjs/core/Animations/animationGroup";
import type { AssetContainer } from "@babylonjs/core/assetContainer";
import { LoadAssetContainerAsync } from "@babylonjs/core/Loading/sceneLoader";
import { StandardMaterial } from "@babylonjs/core/Materials/standardMaterial";
import { Color3 } from "@babylonjs/core/Maths/math.color";
import { TransformNode } from "@babylonjs/core/Meshes/transformNode";
import type { Scene } from "@babylonjs/core/scene";
import { coatOf, type Coat } from "./fur";
import type { FurColor, Pose } from "./types";
import { makeWearable } from "./wearables";

/** The cat's clips (src/models/cat.glb, made by scripts/cat-model.mjs). */
export const catAnimations = ["idle", "walk", "sit", "sleep", "wave", "meow"] as const;
export type CatAnimation = (typeof catAnimations)[number];

/** What a cat is doing, for {@link animationFor}. */
export interface CatDoing {
  moving: boolean;
  pose: Pose | undefined;
  waving: boolean;
  meowing: boolean;
}

/**
 * The clip a cat plays: walking beats everything (a walk ends a pose), a wave shows over a pose for its moment, a pose
 * holds, and a meow shows only on a cat standing (sitting or asleep, its bubble says it).
 */
export function animationFor(doing: CatDoing): CatAnimation {
  if (doing.moving) {
    return "walk";
  }
  if (doing.waving) {
    return "wave";
  }
  if (doing.pose) {
    return doing.pose;
  }
  return doing.meowing ? "meow" : "idle";
}

/**
 * Follows one cat's meows, for whether its meow clip shows. A meow that comes while the cat stands idle shows from its
 * start for as long as it lasts (CatLook.meowingAt), or until the cat does something else; one that comes while the
 * cat walks, waves or holds a pose is only heard (its bubble), then or later.
 */
export class MeowClip {
  private heard = 0;
  private showing = false;

  /**
   * <meows>: how many the cat has made (CatLook.meows); <meowing>: whether the latest still lasts; <otherwise>: the clip
   * the cat would play without it. Whether the meow clip shows now, and whether it starts again.
   */
  next(meows: number, meowing: boolean, otherwise: CatAnimation): { show: boolean; restart: boolean } {
    const fresh = meows !== this.heard;
    this.heard = meows;
    if (fresh) {
      this.showing = otherwise === "idle";
    }
    if (!meowing || otherwise !== "idle") {
      this.showing = false;
    }
    return { show: this.showing, restart: fresh && this.showing };
  }
}

/** Whether a clip plays over and over (meow plays once and holds its last frame). */
export function loops(animation: CatAnimation): boolean {
  return animation !== "meow";
}

/** The turn about the vertical axis (radians) that faces the model, which looks along +z, along (dx, dz). */
export function facing(dx: number, dz: number): number {
  return Math.atan2(dx, dz);
}

/** The model's colour parts: each of its meshes is named "<part>.<colour>". */
const catColours = ["coat", "accent", "eyes", "pupils", "dark"] as const;
export type CatColour = (typeof catColours)[number];

const darkFeatures = "#2a2230";
const eyeColour = "#ffc85a";

/** The colour a part of the model is painted in <coat>; asleep, the eyes are shut (the coat's colour, pupils too). */
export function colourOf(colour: CatColour, coat: Coat, asleep: boolean): string {
  switch (colour) {
    case "coat":
      return coat.body;
    case "accent":
      return coat.accent;
    case "eyes":
      return asleep ? coat.body : eyeColour;
    case "pupils":
      return asleep ? coat.body : darkFeatures;
    case "dark":
      return darkFeatures;
  }
}

/** The colour part a mesh of the model is, from its name ("body.coat", "cat-1-tail.accent"), if it is one. */
export function colourOfMesh(name: string): CatColour | undefined {
  const colour = name.slice(name.lastIndexOf(".") + 1);
  return catColours.find((known) => known === colour);
}

/**
 * Reads the model (the bytes of cat.glb) into <scene>, ready to make cats from. No materials (each cat paints its
 * own), and no clip playing on it: it is only ever cloned.
 */
export async function loadCatModel(scene: Scene, bytes: Uint8Array): Promise<AssetContainer> {
  return LoadAssetContainerAsync(bytes, scene, {
    pluginExtension: ".glb",
    pluginOptions: { gltf: { skipMaterials: true, animationStartMode: GLTFLoaderAnimationStartMode.NONE } },
  });
}

// The model is about 1.25 m wide; cats are a little smaller.
const modelScale = 0.75;
// The dark outline round the coat and what the cat wears, in the model's units.
const outlineWidth = 0.025 / modelScale;

/** What a cat wears: its fur, and the hat and collar it has on (the shop's item IDs), if any. */
export interface Outfit {
  fur: FurColor;
  hat: string | null;
  collar: string | null;
}

/**
 * A modelled cat (a cube cat) in flat, unlit colours with dark outlines, for the hand-drawn look, that walks, idles,
 * sits, sleeps, waves and meows.
 *
 * Attachment points, for what a cat wears: `anchors.headTop` sits on top of the head between the ears (hats), and
 * `anchors.neck` on the front of the chest just under the mouth, at collar height (collars). Both hang off the model's
 * body, so whatever is parented to them follows the animations (it leans back when the cat sits, lies down when it
 * sleeps), and both face the way the cat faces: +y up, +z forward, in the model's units (before the cat's scale).
 * Parent a hat with `hat.parent = cat.anchors.headTop`, modelled with its brim at y = 0; a collar to `neck`, with its
 * buckle at the origin.
 */
export class Cat {
  readonly anchors: { headTop: TransformNode; neck: TransformNode };
  private readonly groups: Map<CatAnimation, AnimationGroup>;
  private readonly materials: ReadonlyMap<CatColour, StandardMaterial>;
  private coat: Coat;
  private fur: FurColor;
  private playing: CatAnimation | undefined;
  // What it has on, and the node drawing each.
  private readonly worn: { hat?: { id: string; node: TransformNode }; collar?: { id: string; node: TransformNode } } = {};

  /** Hangs a new cat of <fur>, made from <model>, off <parent> (which places and turns it). */
  constructor(
    private readonly scene: Scene, model: AssetContainer, parent: TransformNode, private readonly name: string, fur: FurColor,
  ) {
    this.fur = fur;
    this.coat = coatOf(fur);
    const made = model.instantiateModelsToScene((source) => `${name}-${source}`, false, { doNotInstantiate: true });
    const top = made.rootNodes[0] as TransformNode;
    const body = top.getChildTransformNodes(false).find((node) => node.name === `${name}-body`);
    if (!body) {
      made.dispose();
      throw new Error("The cat model has no body");
    }
    top.parent = parent;
    top.scaling.scaleInPlace(modelScale);

    const materials = new Map(catColours.map((colour) =>
      [colour, flat(scene, `${name}-${colour}`, colourOf(colour, this.coat, false))] as const));
    this.materials = materials;
    for (const mesh of top.getChildMeshes(false)) {
      const colour = colourOfMesh(mesh.name);
      const material = colour && materials.get(colour);
      if (!material) {
        continue;
      }
      mesh.material = material;
      mesh.isPickable = false;
      if (colour === "coat" || colour === "accent") {
        mesh.renderOutline = true;
        mesh.outlineWidth = outlineWidth;
        mesh.outlineColor = Color3.FromHexString(darkFeatures);
      } else {
        // The eyes, pupils, mouth and soles are drawn from both sides, as the original model has them: some of their
        // faces are wound the other way.
        material.backFaceCulling = false;
      }
    }
    top.onDisposeObservable.add(() => {
      for (const group of made.animationGroups) {
        group.dispose();
      }
      for (const material of materials.values()) {
        material.dispose();
      }
    });

    this.anchors = {
      headTop: anchor(scene, `${name}-head-top`, body, 0, 1.13, 0.1),
      neck: anchor(scene, `${name}-neck`, body, 0, 0.3, 0.63),
    };

    this.groups = new Map(made.animationGroups.flatMap((group) => {
      const clip = catAnimations.find((known) => group.name === `${name}-${known}`);
      return clip ? [[clip, group] as const] : [];
    }));
    this.play("idle");
  }

  /** The clip playing. */
  get animation(): CatAnimation | undefined {
    return this.playing;
  }

  /** What the cat wears now, as drawn: a hat or collar the client can't draw counts as not worn. */
  get wearing(): { fur: FurColor; hat: string | undefined; collar: string | undefined } {
    return { fur: this.fur, hat: this.worn.hat?.id, collar: this.worn.collar?.id };
  }

  /** Changes what the cat wears: its coat, and the hat on its head and the collar round its neck (or none). */
  dress(outfit: Outfit): void {
    if (outfit.fur !== this.fur) {
      this.fur = outfit.fur;
      this.coat = coatOf(outfit.fur);
      this.paint();
    }
    this.put("hat", outfit.hat, this.anchors.headTop);
    this.put("collar", outfit.collar, this.anchors.neck);
  }

  /** Plays <animation> from its start, unless it is playing already and not to <restart>. */
  play(animation: CatAnimation, restart = false): void {
    if (animation === this.playing && !restart) {
      return;
    }
    for (const group of this.groups.values()) {
      group.stop();
    }
    this.groups.get(animation)?.start(loops(animation));
    this.playing = animation;
    this.paint();
  }

  // The model's colours, for its coat, and its eyes shut while it sleeps.
  private paint(): void {
    for (const [colour, material] of this.materials) {
      material.emissiveColor = Color3.FromHexString(colourOf(colour, this.coat, this.playing === "sleep"));
    }
  }

  // Puts <id> on in <slot> (taking off what was there), or nothing when null.
  private put(slot: "hat" | "collar", id: string | null, anchor: TransformNode): void {
    if (this.worn[slot]?.id === (id ?? undefined)) {
      return;
    }
    this.worn[slot]?.node.dispose();
    this.worn[slot] = undefined;
    const node = id === null ? undefined : makeWearable(this.scene, id, `${this.name}-${slot}`, anchor, outlineWidth);
    if (id !== null && node) {
      this.worn[slot] = { id, node };
    }
  }
}

function anchor(scene: Scene, name: string, parent: TransformNode, x: number, y: number, z: number): TransformNode {
  const node = new TransformNode(name, scene);
  node.parent = parent;
  node.position.set(x, y, z);
  return node;
}

function flat(scene: Scene, name: string, hex: string): StandardMaterial {
  const material = new StandardMaterial(name, scene);
  material.disableLighting = true;
  material.emissiveColor = Color3.FromHexString(hex);
  return material;
}

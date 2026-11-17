// What cats wear from the shop, made in code in the game's flat style: a few low-poly shapes each, flat unlit colours
// and the dark outline the cat has. Nothing to download: a handful of Babylon's mesh builders.
import { StandardMaterial } from "@babylonjs/core/Materials/standardMaterial";
import { Color3 } from "@babylonjs/core/Maths/math.color";
import type { Mesh } from "@babylonjs/core/Meshes/mesh";
import { CreateBox } from "@babylonjs/core/Meshes/Builders/boxBuilder";
import { CreateCylinder } from "@babylonjs/core/Meshes/Builders/cylinderBuilder";
import { CreateSphere } from "@babylonjs/core/Meshes/Builders/sphereBuilder";
import { TransformNode } from "@babylonjs/core/Meshes/transformNode";
import type { Scene } from "@babylonjs/core/scene";

/** The hats there are (the shop's IDs, src/Pspsps.Domain/Shop/ShopCatalog.cs): each is drawn by its maker below. */
export const hatIds = ["party-hat", "bow", "beanie", "top-hat", "crown"] as const;
/** The collars there are. */
export const collarIds = ["red-collar", "blue-collar", "green-collar", "bell-collar"] as const;

const ink = "#2a2230";
const gold = "#f2c230";

/**
 * The shapes, in the cat model's units (before the cat's scale): its head is the top of a 1.25 cube, with the ears at
 * its sides rising about 0.27 above it, so hats sit between them. A hat is modelled with its brim at y = 0, for the
 * head-top anchor; a collar around the body (the cube, centred 0.63 behind the neck anchor), its tag at the origin.
 */
type Maker = (part: (mesh: Mesh, colour: string) => Mesh, scene: Scene, name: string) => void;

const hats: Record<(typeof hatIds)[number], Maker> = {
  "party-hat": (part, scene, name) => {
    const cone = part(CreateCylinder(`${name}-cone`, { height: 0.55, diameterTop: 0, diameterBottom: 0.44, tessellation: 10 }, scene), "#ff7eb6");
    cone.position.y = 0.275;
    const pompom = part(CreateSphere(`${name}-pompom`, { diameter: 0.15, segments: 4 }, scene), "#ffd84d");
    pompom.position.y = 0.57;
  },
  bow: (part, scene, name) => {
    // Two loops (flattened cones, points in) and a knot, a little forward on the head.
    for (const side of [-1, 1]) {
      const loop = part(CreateCylinder(`${name}-loop-${side}`, { height: 0.26, diameterTop: 0.04, diameterBottom: 0.26, tessellation: 8 }, scene), "#e8475f");
      loop.rotation.z = side * Math.PI / 2;
      loop.scaling.z = 0.45;
      loop.position.set(side * 0.15, 0.12, 0.15);
    }
    const knot = part(CreateBox(`${name}-knot`, { width: 0.1, height: 0.12, depth: 0.12 }, scene), "#c7304a");
    knot.position.set(0, 0.12, 0.15);
  },
  beanie: (part, scene, name) => {
    const dome = part(CreateSphere(`${name}-dome`, { diameter: 0.78, segments: 6, slice: 0.5 }, scene), "#4a7bd0");
    dome.scaling.y = 0.75;
    const band = part(CreateCylinder(`${name}-band`, { height: 0.12, diameter: 0.8, tessellation: 12 }, scene), "#3a63ad");
    band.position.y = 0.06;
    const pompom = part(CreateSphere(`${name}-pompom`, { diameter: 0.18, segments: 4 }, scene), "#f6f3ee");
    pompom.position.y = 0.33;
  },
  "top-hat": (part, scene, name) => {
    part(CreateCylinder(`${name}-brim`, { height: 0.04, diameter: 0.72, tessellation: 12 }, scene), "#2b2b33").position.y = 0.02;
    part(CreateCylinder(`${name}-crown`, { height: 0.5, diameter: 0.46, tessellation: 12 }, scene), "#2b2b33").position.y = 0.29;
    part(CreateCylinder(`${name}-band`, { height: 0.08, diameter: 0.475, tessellation: 12 }, scene), "#c7304a").position.y = 0.09;
  },
  crown: (part, scene, name) => {
    part(CreateCylinder(`${name}-band`, { height: 0.18, diameter: 0.52, tessellation: 10 }, scene), gold).position.y = 0.09;
    for (let i = 0; i < 5; i++) {
      const point = part(CreateCylinder(`${name}-point-${i}`, { height: 0.16, diameterTop: 0, diameterBottom: 0.13, tessellation: 4 }, scene), gold);
      const turn = (i / 5) * Math.PI * 2;
      point.position.set(Math.sin(turn) * 0.2, 0.26, Math.cos(turn) * 0.2);
    }
    part(CreateSphere(`${name}-gem`, { diameter: 0.09, segments: 3 }, scene), "#d6304a").position.set(0, 0.09, 0.26);
  },
};

/** A band round the body, a little out from it (clear of the coat's outline), and what hangs at the front. */
function collar(colour: string, bell: boolean): Maker {
  return (part, scene, name) => {
    const band = part(CreateBox(`${name}-band`, { width: 1.37, height: 0.11, depth: 1.37 }, scene), colour);
    band.position.z = -0.63;
    if (bell) {
      part(CreateSphere(`${name}-bell`, { diameter: 0.2, segments: 4 }, scene), gold).position.set(0, -0.1, 0.1);
    } else {
      const tag = part(CreateCylinder(`${name}-tag`, { height: 0.03, diameter: 0.14, tessellation: 8 }, scene), "#d9dde3");
      tag.rotation.x = Math.PI / 2;
      tag.position.set(0, -0.09, 0.07);
    }
  };
}

const collars: Record<(typeof collarIds)[number], Maker> = {
  "red-collar": collar("#d64545", false),
  "blue-collar": collar("#3f7fd6", false),
  "green-collar": collar("#4caf62", false),
  "bell-collar": collar("#d64545", true),
};

/** Whether the client can draw <id> (a hat or collar it knows): an unknown one (a newer server's) is left off. */
export function canDraw(id: string): boolean {
  return id in hats || id in collars;
}

/**
 * Makes the hat or collar <id> under <parent> (a cat's anchor), outlined like the cat (<outline>, in the anchor's
 * units). Undefined for an ID the client doesn't know. Disposing the node (or the cat) disposes its meshes and
 * materials.
 */
export function makeWearable(scene: Scene, id: string, name: string, parent: TransformNode, outline: number): TransformNode | undefined {
  const maker = (hats as Record<string, Maker>)[id] ?? (collars as Record<string, Maker>)[id];
  if (!maker) {
    return undefined;
  }
  const root = new TransformNode(name, scene);
  root.parent = parent;
  // Its materials go with it, however it goes: taken off, or with the cat (a node disposes its children, not their
  // materials).
  const materials: StandardMaterial[] = [];
  root.onDisposeObservable.add(() => {
    for (const material of materials) {
      material.dispose();
    }
  });
  const part = (mesh: Mesh, colour: string): Mesh => {
    const material = new StandardMaterial(`${mesh.name}-material`, scene);
    materials.push(material);
    material.disableLighting = true;
    material.emissiveColor = Color3.FromHexString(colour);
    mesh.material = material;
    mesh.parent = root;
    mesh.renderOutline = true;
    mesh.outlineWidth = outline;
    mesh.outlineColor = Color3.FromHexString(ink);
    return mesh;
  };
  maker(part, scene, name);
  return root;
}

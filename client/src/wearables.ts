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
 * The shapes, in the cat model's units (before the cat's scale). The cat is a cube 1.25 a side with its edges bevelled:
 * its head's top is flat, and the ears stand up from its front corners (from about 0.2 either side of the middle, and
 * 0.19 in front of the head-top anchor). A hat is modelled with its brim at y = 0, on the head-top anchor, and kept
 * behind the ears; a collar goes round the body (centred 0.63 behind the neck anchor), its tag at the origin.
 * wearables.fit.test.ts measures them against the model.
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
    // Two loops (flattened cones, points in) and a knot, tied on the top of the head just behind the ears.
    for (const side of [-1, 1]) {
      const loop = part(CreateCylinder(`${name}-loop-${side}`, { height: 0.26, diameterTop: 0.04, diameterBottom: 0.26, tessellation: 8 }, scene), "#e8475f");
      loop.rotation.z = side * Math.PI / 2;
      loop.scaling.z = 0.45;
      loop.position.set(side * 0.15, 0.13, 0);
    }
    const knot = part(CreateBox(`${name}-knot`, { width: 0.1, height: 0.12, depth: 0.12 }, scene), "#c7304a");
    knot.position.set(0, 0.13, 0);
  },
  beanie: (part, scene, name) => {
    // Snug on the flat of the head, between and behind the ears: a dome on a ribbed band, and a pompom.
    const back = -0.12;
    const dome = part(CreateSphere(`${name}-dome`, { diameter: 0.6, segments: 6, slice: 0.5 }, scene), "#4a7bd0");
    dome.scaling.y = 0.8;
    dome.position.set(0, 0.08, back);
    const band = part(CreateCylinder(`${name}-band`, { height: 0.1, diameter: 0.62, tessellation: 12 }, scene), "#3a63ad");
    band.position.set(0, 0.05, back);
    const pompom = part(CreateSphere(`${name}-pompom`, { diameter: 0.17, segments: 4 }, scene), "#f6f3ee");
    pompom.position.set(0, 0.35, back);
  },
  "top-hat": (part, scene, name) => {
    const back = -0.1;
    part(CreateCylinder(`${name}-brim`, { height: 0.04, diameter: 0.62, tessellation: 12 }, scene), "#2b2b33").position.set(0, 0.02, back);
    part(CreateCylinder(`${name}-crown`, { height: 0.5, diameter: 0.42, tessellation: 12 }, scene), "#2b2b33").position.set(0, 0.29, back);
    // The ribbon stands out from the crown past its outline, which would hide anything closer.
    part(CreateCylinder(`${name}-band`, { height: 0.08, diameter: 0.5, tessellation: 12 }, scene), "#c7304a").position.set(0, 0.09, back);
  },
  crown: (part, scene, name) => {
    const back = -0.05;
    part(CreateCylinder(`${name}-band`, { height: 0.18, diameter: 0.52, tessellation: 10 }, scene), gold).position.set(0, 0.09, back);
    for (let i = 0; i < 5; i++) {
      const point = part(CreateCylinder(`${name}-point-${i}`, { height: 0.16, diameterTop: 0, diameterBottom: 0.13, tessellation: 4 }, scene), gold);
      const turn = (i / 5) * Math.PI * 2;
      point.position.set(Math.sin(turn) * 0.2, 0.26, back + Math.cos(turn) * 0.2);
    }
    part(CreateSphere(`${name}-gem`, { diameter: 0.09, segments: 3 }, scene), "#d6304a").position.set(0, 0.09, back + 0.26);
  },
};

/**
 * The band of a collar, seen from above: a regular octagon, its flat sides on the cat's sides and on the cube's cut-off
 * upright edges, this far out from the middle. The body there is an octagon too, its sides 0.625 out and its cut-off
 * edges 0.66: the band clears both by the coat's outline (0.033) and more, as the outline is drawn pulled towards the
 * camera and shows through a band nearer than that; and by little more, so it hugs the cat all round.
 */
const collarApothem = 0.735;
const collarHeight = 0.1;

/** A band round the body, following its shape just clear of it, and what hangs at the front. */
function collar(colour: string, bell: boolean): Maker {
  return (part, scene, name) => {
    const band = part(CreateCylinder(`${name}-band`, {
      height: collarHeight, diameter: (2 * collarApothem) / Math.cos(Math.PI / 8), tessellation: 8,
    }, scene), colour);
    // Its sides square to the cat's (a cylinder's first corner points along x).
    band.rotation.y = Math.PI / 8;
    band.position.z = -0.63;
    if (bell) {
      part(CreateSphere(`${name}-bell`, { diameter: 0.2, segments: 4 }, scene), gold).position.set(0, -0.08, 0.2);
    } else {
      const tag = part(CreateCylinder(`${name}-tag`, { height: 0.03, diameter: 0.14, tessellation: 8 }, scene), "#d9dde3");
      tag.rotation.x = Math.PI / 2;
      tag.position.set(0, -0.08, 0.13);
    }
  };
}

const collars: Record<(typeof collarIds)[number], Maker> = {
  "red-collar": collar("#d64545", false),
  "blue-collar": collar("#3f7fd6", false),
  "green-collar": collar("#4caf62", false),
  "bell-collar": collar("#d64545", true),
};

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

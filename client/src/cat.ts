import { StandardMaterial } from "@babylonjs/core/Materials/standardMaterial";
import { Color3 } from "@babylonjs/core/Maths/math.color";
import type { AbstractMesh } from "@babylonjs/core/Meshes/abstractMesh";
import { CreateCylinder } from "@babylonjs/core/Meshes/Builders/cylinderBuilder";
import { CreateSphere } from "@babylonjs/core/Meshes/Builders/sphereBuilder";
import { TransformNode } from "@babylonjs/core/Meshes/transformNode";
import type { Scene } from "@babylonjs/core/scene";
import { coatOf } from "./fur";
import type { FurColor } from "./types";

const outline = Color3.FromHexString("#2a2230");

/**
 * A cat built from primitives in flat, unlit colours with dark outlines, for the hand-drawn look. A placeholder until
 * there's a modelled cat. The parts hang off `body`, which poses squash; `root` stays unscaled for whatever is fixed
 * above or beside the cat (its name, bubbles) and for `paw`, hidden until the cat waves.
 */
export function buildCat(
  scene: Scene, name: string, fur: FurColor,
): { root: TransformNode; body: TransformNode; paw: AbstractMesh } {
  const coat = coatOf(fur);
  const main = flat(scene, `${name}-coat`, coat.body);
  const accent = flat(scene, `${name}-accent`, coat.accent);
  const eyes = flat(scene, `${name}-eyes`, "#1b1b1f");
  const root = new TransformNode(name, scene);
  const body = new TransformNode(`${name}-body`, scene);
  body.parent = root;

  const parts = [
    part(CreateSphere("body", { diameterX: 0.9, diameterY: 0.8, diameterZ: 1.2 }, scene), main, 0, 0.45, 0),
    part(CreateSphere("chest", { diameter: 0.5 }, scene), accent, 0, 0.5, 0.42),
    part(CreateSphere("head", { diameter: 0.75 }, scene), main, 0, 1.05, 0.45),
    part(CreateCylinder("ear-l", { height: 0.32, diameterTop: 0, diameterBottom: 0.26 }, scene), accent, -0.2, 1.42, 0.45),
    part(CreateCylinder("ear-r", { height: 0.32, diameterTop: 0, diameterBottom: 0.26 }, scene), accent, 0.2, 1.42, 0.45),
    part(CreateSphere("eye-l", { diameter: 0.1 }, scene), eyes, -0.14, 1.1, 0.8),
    part(CreateSphere("eye-r", { diameter: 0.1 }, scene), eyes, 0.14, 1.1, 0.8),
    part(CreateCylinder("tail", { height: 0.9, diameterTop: 0.08, diameterBottom: 0.14 }, scene), accent, 0, 0.75, -0.7),
  ];
  for (const mesh of parts) {
    mesh.parent = body;
    mesh.isPickable = false;
    if (!mesh.name.startsWith("eye")) {
      outlined(mesh);
    }
  }
  const tail = parts.at(-1);
  if (tail) {
    tail.rotation.x = -0.6;
  }

  const paw = part(CreateSphere("paw", { diameter: 0.24 }, scene), main, 0.42, 1.2, 0.55);
  paw.parent = root;
  paw.isPickable = false;
  outlined(paw);
  paw.setEnabled(false);
  return { root, body, paw };
}

function outlined(mesh: AbstractMesh): void {
  mesh.renderOutline = true;
  mesh.outlineWidth = 0.025;
  mesh.outlineColor = outline;
}

function flat(scene: Scene, name: string, hex: string): StandardMaterial {
  const material = new StandardMaterial(name, scene);
  material.disableLighting = true;
  material.emissiveColor = Color3.FromHexString(hex);
  return material;
}

function part<T extends { position: { set(x: number, y: number, z: number): void }; material: unknown }>(
  mesh: T, material: StandardMaterial, x: number, y: number, z: number,
): T {
  mesh.material = material;
  mesh.position.set(x, y, z);
  return mesh;
}

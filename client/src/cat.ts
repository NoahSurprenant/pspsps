import { Color3, MeshBuilder, StandardMaterial, TransformNode, type Scene } from "@babylonjs/core";
import { coatOf } from "./fur";
import type { FurColor } from "./types";

const outline = Color3.FromHexString("#2a2230");

/**
 * A cat built from primitives in flat, unlit colours with dark outlines, for the hand-drawn look. A placeholder until
 * there's a modelled cat.
 */
export function buildCat(scene: Scene, name: string, fur: FurColor): TransformNode {
  const coat = coatOf(fur);
  const body = flat(scene, `${name}-body`, coat.body);
  const accent = flat(scene, `${name}-accent`, coat.accent);
  const eyes = flat(scene, `${name}-eyes`, "#1b1b1f");
  const root = new TransformNode(name, scene);

  const parts = [
    part(MeshBuilder.CreateSphere("body", { diameterX: 0.9, diameterY: 0.8, diameterZ: 1.2 }, scene), body, 0, 0.45, 0),
    part(MeshBuilder.CreateSphere("chest", { diameter: 0.5 }, scene), accent, 0, 0.5, 0.42),
    part(MeshBuilder.CreateSphere("head", { diameter: 0.75 }, scene), body, 0, 1.05, 0.45),
    part(MeshBuilder.CreateCylinder("ear-l", { height: 0.32, diameterTop: 0, diameterBottom: 0.26 }, scene), accent, -0.2, 1.42, 0.45),
    part(MeshBuilder.CreateCylinder("ear-r", { height: 0.32, diameterTop: 0, diameterBottom: 0.26 }, scene), accent, 0.2, 1.42, 0.45),
    part(MeshBuilder.CreateSphere("eye-l", { diameter: 0.1 }, scene), eyes, -0.14, 1.1, 0.8),
    part(MeshBuilder.CreateSphere("eye-r", { diameter: 0.1 }, scene), eyes, 0.14, 1.1, 0.8),
    part(MeshBuilder.CreateCylinder("tail", { height: 0.9, diameterTop: 0.08, diameterBottom: 0.14 }, scene), accent, 0, 0.75, -0.7),
  ];
  for (const mesh of parts) {
    mesh.parent = root;
    mesh.isPickable = false;
    if (!mesh.name.startsWith("eye")) {
      mesh.renderOutline = true;
      mesh.outlineWidth = 0.025;
      mesh.outlineColor = outline;
    }
  }
  const tail = parts.at(-1);
  if (tail) {
    tail.rotation.x = -0.6;
  }
  return root;
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

// The island as a small 3D diorama, seen from a fixed orthographic camera. It is a picture: the buttons over it (worldMap.ts)
// are what cats use. Babylon by module, as scene.ts does.
import { ArcRotateCamera } from "@babylonjs/core/Cameras/arcRotateCamera";
import { Camera } from "@babylonjs/core/Cameras/camera";
import type { AbstractEngine } from "@babylonjs/core/Engines/abstractEngine";
import { DirectionalLight } from "@babylonjs/core/Lights/directionalLight";
import { HemisphericLight } from "@babylonjs/core/Lights/hemisphericLight";
import { StandardMaterial } from "@babylonjs/core/Materials/standardMaterial";
import { Color3, Color4 } from "@babylonjs/core/Maths/math.color";
import { Vector3 } from "@babylonjs/core/Maths/math.vector";
import { CreateBox } from "@babylonjs/core/Meshes/Builders/boxBuilder";
import { CreateCylinder } from "@babylonjs/core/Meshes/Builders/cylinderBuilder";
import { CreateSphere } from "@babylonjs/core/Meshes/Builders/sphereBuilder";
import type { Mesh } from "@babylonjs/core/Meshes/mesh";
import { TransformNode } from "@babylonjs/core/Meshes/transformNode";
import { Scene } from "@babylonjs/core/scene";
import { VIEW, matches, type Filter } from "./islandMap";
import type { MapPlaceView } from "./types";

export interface IslandScene {
  /** Shrinks the places that aren't in the tab, so the ones that are stand out. */
  filter(filter: Filter): void;
  resize(): void;
  dispose(): void;
}

const colours = {
  sea: "#7cc4dc", sand: "#ecd9a6", grass: "#a9d276", path: "#e8d3a0", wood: "#8a5a3c", leaf: "#4f9a58", lightLeaf: "#78b865",
  stone: "#b9b4a8", rock: "#8c8479", snow: "#f4f6f8", white: "#f7f2e8", red: "#d6584f", roof: "#b9614a", water: "#58b4d6",
  yellow: "#f1c85a", pink: "#e98aa4", blue: "#6a8fd6", dark: "#3f4a3c",
};

export function drawIsland(engine: AbstractEngine, places: readonly MapPlaceView[]): IslandScene {
  const scene = new Scene(engine);
  scene.clearColor = Color4.FromColor3(Color3.FromHexString(colours.sea), 1);
  scene.autoClear = true;
  const camera = new ArcRotateCamera("island", -Math.PI / 2, Math.PI / 2 - VIEW.elevation, 200, Vector3.Zero(), scene);
  camera.mode = Camera.ORTHOGRAPHIC_CAMERA;
  camera.orthoLeft = -VIEW.halfWidth;
  camera.orthoRight = VIEW.halfWidth;
  camera.orthoBottom = -VIEW.halfHeight;
  camera.orthoTop = VIEW.halfHeight;
  camera.minZ = 1;
  camera.maxZ = 500;
  const sun = new HemisphericLight("sky", new Vector3(0.2, 1, -0.3), scene);
  sun.intensity = 0.85;
  sun.groundColor = new Color3(0.55, 0.6, 0.65);
  new DirectionalLight("sun", new Vector3(-0.4, -1, 0.5), scene).intensity = 0.55;

  const materials = new Map<string, StandardMaterial>();
  const paint = (mesh: Mesh, hex: string): Mesh => {
    let material = materials.get(hex);
    if (!material) {
      material = new StandardMaterial(hex, scene);
      material.diffuseColor = Color3.FromHexString(hex);
      material.specularColor = Color3.Black();
      materials.set(hex, material);
    }
    mesh.material = material;
    return mesh;
  };

  // Water, then sand, then grass, a little bigger each: a green island in the sea.
  const disc = (parent: TransformNode | null, diameter: number, depth: number, height: number, y: number, hex: string, x = 0, z = 0): Mesh => {
    const mesh = CreateCylinder("disc", { height, diameter: 1, tessellation: 48 }, scene);
    mesh.scaling.set(diameter, 1, depth);
    mesh.position.set(x, y, z);
    if (parent) mesh.parent = parent;
    return paint(mesh, hex);
  };
  disc(null, 98, 90, 0.6, -0.35, colours.sand);
  disc(null, 90, 82, 1, -0.5, colours.grass);

  const at = (parent: TransformNode, mesh: Mesh, x: number, y: number, z: number, hex: string): Mesh => {
    mesh.parent = parent;
    mesh.position.set(x, y, z);
    return paint(mesh, hex);
  };
  const box = (p: TransformNode, w: number, h: number, d: number, x: number, y: number, z: number, hex: string) =>
    at(p, CreateBox("box", { width: w, height: h, depth: d }, scene), x, y + h / 2, z, hex);
  const cone = (p: TransformNode, bottom: number, h: number, x: number, y: number, z: number, hex: string, top = 0) =>
    at(p, CreateCylinder("cone", { diameterBottom: bottom, diameterTop: top, height: h, tessellation: 20 }, scene), x, y + h / 2, z, hex);
  const ball = (p: TransformNode, diameter: number, x: number, y: number, z: number, hex: string) =>
    at(p, CreateSphere("ball", { diameter, segments: 10 }, scene), x, y + diameter / 2, z, hex);
  const tree = (p: TransformNode, x: number, z: number, size = 1, hex = colours.leaf) => {
    cone(p, 0.7 * size, 2 * size, x, 0, z, colours.wood, 0.5 * size);
    cone(p, 3.2 * size, 4 * size, x, 1.6 * size, z, hex);
  };
  const house = (p: TransformNode, x: number, z: number, wall: string, roof: string) => {
    box(p, 3.4, 2.4, 3, x, 0, z, wall);
    cone(p, 5, 2, x, 2.4, z, roof).rotation.y = Math.PI / 4;
  };

  const landmarks: Record<string, (p: TransformNode) => void> = {
    plaza: (p) => {
      disc(p, 14, 12, 0.2, 0.1, colours.stone);
      disc(p, 4, 4, 1, 0.6, colours.white);
      disc(p, 2.4, 2.4, 0.2, 1.1, colours.water);
      box(p, 3, 3.4, 2.4, -6, 0, 6, colours.pink);
      box(p, 3, 3.4, 2.4, 6, 0, 6, colours.yellow);
    },
    garden: (p) => {
      box(p, 10, 0.8, 0.8, 0, 0, 4, colours.leaf);
      box(p, 10, 0.8, 0.8, 0, 0, -4, colours.leaf);
      for (const [x, z, hex] of [[-2.5, -1, colours.pink], [0, 1, colours.yellow], [2.5, -1, colours.red], [-1, 2, colours.blue]] as const) {
        disc(p, 2, 2, 0.5, 0.25, colours.wood, x, z);
        ball(p, 1.2, x, 0.4, z, hex);
      }
    },
    orchard: (p) => {
      for (const [x, z] of [[-4, -3], [0, 2], [4, -2], [-3, 4], [5, 4]] as const) {
        tree(p, x, z, 0.8, colours.lightLeaf);
        ball(p, 0.7, x + 1, 1.6, z - 1, colours.yellow);
      }
    },
    homes: (p) => {
      house(p, -5, 0, colours.white, colours.red);
      house(p, 0, 2, colours.yellow, colours.blue);
      house(p, 5, 0, colours.pink, colours.roof);
    },
    lighthouse: (p) => {
      disc(p, 8, 8, 0.4, 0.2, colours.rock);
      cone(p, 4, 9, 0, 0.4, 0, colours.white, 2.6);
      cone(p, 3.4, 1.6, 0, 9.4, 0, colours.red);
      ball(p, 1.4, 0, 8.6, 0, colours.yellow);
    },
    river: (p) => {
      box(p, 3, 0.25, 14, 0, 0, 0, colours.water).rotation.y = 0.35;
      box(p, 6, 0.5, 2.2, 0, 0.1, 0, colours.wood).rotation.y = 0.35;
      tree(p, -5, 4, 0.7);
      tree(p, 5, -4, 0.7);
    },
    cove: (p) => {
      disc(p, 14, 11, 0.3, 0.15, colours.sand);
      disc(p, 7, 5, 0.3, 0.3, colours.water, 1, -1);
      box(p, 8, 0.4, 2, -2, 0.15, 3.5, colours.wood);
      box(p, 3.6, 0.7, 1.4, 4, 0.5, 1.8, colours.red);
    },
    mountain: (p) => {
      for (const [x, z, size] of [[0, 0, 1.4], [-6, -2, 1], [6, -1, 1.1]] as const) {
        cone(p, 10 * size, 11 * size, x, 0, z, colours.rock);
        cone(p, 3.8 * size, 3.6 * size, x, 7.4 * size, z, colours.snow);
      }
    },
    meadow: (p) => {
      disc(p, 16, 12, 0.2, 0.1, colours.lightLeaf);
      for (const [x, z, hex] of [[-4, -1, colours.pink], [0, 2, colours.blue], [3, -2, colours.yellow], [5, 2, colours.red]] as const) {
        ball(p, 1.8, x, 0.2, z, hex);
      }
    },
    market: (p) => {
      for (const [x, hex] of [[-4.5, colours.red], [0, colours.blue], [4.5, colours.yellow]] as const) {
        box(p, 3.6, 0.9, 2.2, x, 0, 0, colours.wood);
        box(p, 4, 0.4, 3, x, 2.6, -0.2, hex);
        box(p, 0.3, 2.6, 0.3, x - 1.7, 0, 1, colours.wood);
        box(p, 0.3, 2.6, 0.3, x + 1.7, 0, 1, colours.wood);
      }
    },
    mill: (p) => {
      cone(p, 5.5, 8, 0, 0, 0, colours.white, 3.6);
      cone(p, 4.4, 2.2, 0, 8, 0, colours.roof);
      box(p, 9, 0.5, 0.3, 0, 6.2, -2.4, colours.wood);
      box(p, 0.5, 9, 0.3, 0, 1.7, -2.4, colours.wood);
    },
    woods: (p) => {
      for (const [x, z, size] of [[-5, -2, 1.2], [-1, 3, 1], [3, -3, 1.3], [6, 2, 0.9], [-4, 5, 0.9], [1, -6, 1]] as const) {
        tree(p, x, z, size, colours.leaf);
      }
    },
    hollow: (p) => {
      const mound = ball(p, 10, 0, -2.5, 0, colours.lightLeaf);
      mound.scaling.y = 0.55;
      ball(p, 2.4, 0, 0.2, -3.8, colours.dark).scaling.y = 1.3;
      tree(p, -6, 2, 0.7);
    },
    dojo: (p) => {
      disc(p, 12, 9, 0.3, 0.15, colours.rock);
      box(p, 5, 2.4, 4, 0, 0.3, -1, colours.white);
      box(p, 6.4, 0.5, 5.4, 0, 2.7, -1, colours.roof);
      box(p, 0.5, 4.4, 0.5, -3.2, 0.3, 3.4, colours.red);
      box(p, 0.5, 4.4, 0.5, 3.2, 0.3, 3.4, colours.red);
      box(p, 8, 0.6, 0.7, 0, 4.2, 3.4, colours.red);
    },
    rink: (p) => {
      disc(p, 15, 10, 0.25, 0.1, colours.leaf);
      disc(p, 12, 7, 0.25, 0.3, colours.snow);
      for (const x of [-6.6, 6.6]) {
        box(p, 0.4, 1.6, 2.4, x, 0.1, 0, colours.white);
      }
    },
    mine: (p) => {
      const rocks = ball(p, 11, 0, -3, -1, colours.rock);
      rocks.scaling.y = 0.55;
      box(p, 4, 2.6, 3.4, 0, 0, 2.4, colours.wood);
      box(p, 4.8, 0.5, 4.2, 0, 2.6, 2.4, colours.roof);
      box(p, 2.2, 2.4, 0.4, 0, 0.2, -3.2, colours.dark);
    },
  };

  const nodes = new Map<string, TransformNode>();
  for (const place of places) {
    const node = new TransformNode(place.id, scene);
    node.position.set(place.x, 0, place.z);
    (landmarks[place.landmark] ?? landmarks.plaza)?.(node);
    nodes.set(place.id, node);
  }

  let dirty = true;
  let disposed = false;
  engine.runRenderLoop(() => {
    if (dirty && !disposed) {
      dirty = false;
      scene.render();
    }
  });
  return {
    filter(filter) {
      for (const place of places) {
        nodes.get(place.id)?.scaling.setAll(matches(place, filter) ? 1 : 0.45);
      }
      dirty = true;
    },
    resize() {
      engine.resize();
      dirty = true;
    },
    dispose() {
      disposed = true;
      engine.stopRenderLoop();
      scene.dispose();
      engine.dispose();
    },
  };
}

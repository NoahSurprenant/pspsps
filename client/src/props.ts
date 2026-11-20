// The rooms' props and backdrops, drawn in the game's flat, illustrated style: each part a flat colour (its top a little
// lighter, so a box reads as a box), with the dark outline every room has. The outline is the part again, a little
// larger and turned inside out (only its far side is drawn, round the part's edge), so it costs no extra pass: unlike
// renderOutline, whose pass, from far off, covered what lay flat on the floor. A room's parts are merged into one mesh
// with one material: one draw call, however many props.
import { StandardMaterial } from "@babylonjs/core/Materials/standardMaterial";
import { Color3 } from "@babylonjs/core/Maths/math.color";
import { Matrix, Quaternion, Vector3 } from "@babylonjs/core/Maths/math.vector";
import { CreateBoxVertexData } from "@babylonjs/core/Meshes/Builders/boxBuilder";
import { CreateCylinderVertexData } from "@babylonjs/core/Meshes/Builders/cylinderBuilder";
import { CreateIcoSphereVertexData } from "@babylonjs/core/Meshes/Builders/icoSphereBuilder";
import { Mesh } from "@babylonjs/core/Meshes/mesh";
import { VertexData } from "@babylonjs/core/Meshes/mesh.vertexData";
import type { Scene } from "@babylonjs/core/scene";
import type { PropView } from "./types";

/** The dark ink every outline in a room is drawn in. */
export const ink = "#2a2230";
/** How thick a part's outline is, in metres. */
export const outline = 0.05;

/**
 * One part of a prop, in metres, in the prop's own space: standing on the floor at the origin, its front towards -Z (the
 * camera). Rotations are in radians about each axis.
 */
export type Part = (
  | { shape: "box"; width: number; height: number; depth: number }
  | { shape: "cylinder"; diameter: number; height: number; top?: number; sides?: number }
  | { shape: "ball"; diameter: number; height?: number }
) & { at: [number, number, number]; turn?: [number, number, number]; colour: string; outlined?: boolean };

// The props' colours. None is the pond's blue (#5fb3d9), a door mat's red (#c8553d) or a cat's eye yellow (#ffc85a): the
// end-to-end tests look for those (paint.test.ts keeps it so, the lighter tops included).
export const palette = {
  wood: "#c98f5a",
  darkWood: "#7a5a44",
  iron: "#3b3442",
  leaf: "#6fae5f",
  lightLeaf: "#8cc36f",
  glow: "#fff3c4",
  stone: "#d8cbb5",
  soil: "#6b4f3f",
  pink: "#f29bc1",
  white: "#fdf7f0",
  lilac: "#a98bd6",
  butter: "#f6e27a",
  cream: "#f4e9d8",
  sage: "#8fb9a8",
  mustard: "#cc9a3a",
  teal: "#4f7f86",
  silver: "#c9ccd1",
  rose: "#f2a7c3",
  sisal: "#e3cfa3",
  lavender: "#b9a7d4",
  pot: "#efe4d0",
  mirror: "#d8eef0",
  hatPink: "#f58fbf",
  felt: "#3d3a4a",
} as const;

const box = (width: number, height: number, depth: number, at: [number, number, number], colour: string,
  turn?: [number, number, number]): Part => ({ shape: "box", width, height, depth, at, colour, turn });
const ball = (diameter: number, at: [number, number, number], colour: string, height?: number): Part =>
  ({ shape: "ball", diameter, height, at, colour });


/** What each kind of prop is drawn from, at the size it is designed for (<size>: the radius it fits in, outline included); the server's, from /api/props. */
export interface PropDesign {
  kind: string;
  size: number;
  parts: Part[];
}

const designs = new Map<string, PropDesign>();

/** The kinds of prop drawn here: those the server has a design for. */
export const propKinds: string[] = [];

export function setPropDesigns(list: readonly PropDesign[]): void {
  designs.clear();
  propKinds.length = 0;
  for (const design of list) {
    designs.set(design.kind, design);
    propKinds.push(design.kind);
  }
}

/** Reads the designs from the server: every page load, so a new design or kind needs no new client. */
export async function loadPropDesigns(): Promise<void> {
  const response = await fetch("/api/props");
  if (!response.ok) {
    throw new Error(`The prop designs could not be read (${response.status}).`);
  }
  setPropDesigns((await response.json()) as PropDesign[]);
}

/** A prop's parts, sized to fit its radius, turned to face its way and stood on its spot; none for a kind without a design. */
function pears(x: number, y: number, z: number): Part[] {
  return [ball(0.36, [x, y, z], "#ebbf55", 0.42), ball(0.24, [x, y + 0.2, z], "#ebbf55"),
    box(0.05, 0.16, 0.05, [x, y + 0.36, z], palette.darkWood, [0, 0, -0.2])];
}

export function propParts(prop: PropView, stage = 0): Part[] {
  const design = designs.get(prop.kind);
  if (!design) {
    return [];
  }
  // The outline is the same width at any size, so what is drawn inside it scales to leave room for it.
  const scale = Math.max(0, prop.radius - outline) / (design.size - outline);
  const turn = Matrix.RotationY(prop.facing);
  const parts = [...design.parts];
  if (prop.kind === "pear-tree" && stage === 0) {
    for (const [x, y, z] of [[-0.57, 2.05, -0.65], [0.48, 1.88, -0.72], [0, 2.87, -0.64]] as const) {
      parts.push(...pears(x, y, z));
    }
  }
  if (prop.kind === "packing-crate") {
    for (let i = 0; i < stage; i++) parts.push(...pears((i % 3 - 1) * 0.5, 0.75, 0));
  }
  if (prop.kind === "potting-bench") {
    for (let i = 0; i < stage; i++) {
      const x = (i - 1) * 0.58;
      parts.push(box(0.06, 0.45, 0.06, [x, 1.54, 0], palette.leaf));
      for (let p = 0; p < 5; p++) {
        const a = p * 2 * Math.PI / 5;
        parts.push(ball(0.22, [x + 0.14 * Math.cos(a), 1.82 + 0.14 * Math.sin(a), -0.06], palette.rose));
      }
      parts.push(ball(0.17, [x, 1.82, -0.17], palette.butter));
    }
  }
  return parts.map((part) => {
    const at = Vector3.TransformCoordinates(new Vector3(...part.at).scale(scale), turn);
    return {
      ...scaled(part, scale),
      at: [at.x + prop.x, at.y, at.z + prop.z],
      turn: [part.turn?.[0] ?? 0, (part.turn?.[1] ?? 0) + prop.facing, part.turn?.[2] ?? 0],
    };
  });
}

function scaled(part: Part, by: number): Part {
  switch (part.shape) {
    case "box":
      return { ...part, width: part.width * by, height: part.height * by, depth: part.depth * by };
    case "cylinder":
      return { ...part, diameter: part.diameter * by, height: part.height * by, top: part.top === undefined ? undefined : part.top * by };
    case "ball":
      return { ...part, diameter: part.diameter * by, height: part.height === undefined ? undefined : part.height * by };
  }
}

/** Builds <parts> into one mesh with one flat material, each part outlined in ink (unless it says otherwise). */
export function buildParts(scene: Scene, name: string, parts: readonly Part[]): Mesh | undefined {
  const pieces: VertexData[] = [];
  for (const part of parts) {
    pieces.push(piece(part, 0, part.colour));
    if (part.outlined !== false) {
      pieces.push(insideOut(piece(part, outline, ink)));
    }
  }
  const [first, ...rest] = pieces;
  if (!first) {
    return undefined;
  }
  const merged = first.merge(rest, true);
  const mesh = new Mesh(name, scene);
  merged.applyToMesh(mesh);
  mesh.useVertexColors = true;
  const material = new StandardMaterial(name, scene);
  // Unlit: each vertex's colour as it is.
  material.disableLighting = true;
  material.emissiveColor = Color3.White();
  mesh.material = material;
  // Built once and never moved: no need to recompute where it is each frame.
  mesh.freezeWorldMatrix();
  return mesh;
}

/** A part's vertices, <grow> metres larger all round, coloured <hex> (the tops a touch lighter). */
function piece(part: Part, grow: number, hex: string): VertexData {
  const data = shapeOf(part, grow);
  const turn = part.turn ?? [0, 0, 0];
  data.transform(Matrix.Compose(Vector3.One(), Quaternion.FromEulerAngles(...turn), new Vector3(...part.at)));
  const colour = Color3.FromHexString(hex);
  const top = grow > 0 ? colour : Color3.FromHexString(topOf(hex));
  const normals = data.normals ?? [];
  const count = (data.positions?.length ?? 0) / 3;
  const colours = new Float32Array(count * 4);
  for (let i = 0; i < count; i++) {
    const c = (normals[i * 3 + 1] ?? 0) > 0.7 ? top : colour;
    colours.set([c.r, c.g, c.b, 1], i * 4);
  }
  data.colors = colours;
  return data;
}

function shapeOf(part: Part, grow: number): VertexData {
  const twice = grow * 2;
  switch (part.shape) {
    case "box":
      return CreateBoxVertexData({ width: part.width + twice, height: part.height + twice, depth: part.depth + twice });
    case "cylinder":
      return CreateCylinderVertexData({
        diameterBottom: part.diameter + twice,
        diameterTop: (part.top ?? part.diameter) + twice,
        height: part.height + twice,
        tessellation: part.sides ?? 12,
      });
    case "ball": {
      const radius = part.diameter / 2 + grow;
      return CreateIcoSphereVertexData({
        // A small ball (a flower) needs fewer facets to look round: fewer triangles to draw, and for a tap to test.
        radiusX: radius, radiusZ: radius, radiusY: (part.height ?? part.diameter) / 2 + grow, subdivisions: part.diameter < 0.5 ? 1 : 2,
        flat: true,
      });
    }
  }
}

/** The same shape turned inside out: only its far side is drawn, which shows as an edge round the part in front of it. */
function insideOut(data: VertexData): VertexData {
  const indices = data.indices ?? [];
  for (let i = 0; i + 2 < indices.length; i += 3) {
    [indices[i + 1], indices[i + 2]] = [indices[i + 2] ?? 0, indices[i + 1] ?? 0];
  }
  return data;
}

/** The colour a part's top is drawn in: its own a little lighter, so a box reads as a box. */
export function topOf(hex: string): string {
  return Color3.Lerp(Color3.FromHexString(hex), Color3.White(), 0.18).toHexString().toLowerCase();
}

/** The prop whose circle the spot (x, z) on the floor is in, if any. */
export function propAt(props: readonly PropView[], x: number, z: number): PropView | undefined {
  return props.find((p) => Math.hypot(x - p.x, z - p.z) <= p.radius);
}

/** How far past a prop's edge a cat sent beside it stands. */
const besideGap = 0.4;

/**
 * Where a tap at (x, z) on a prop (the foot of the point tapped: on a tree's crown, the floor under it) walks the cat:
 * to the floor just beside the prop, on the side tapped, or (tapped in the middle, or that side is taken) in front of
 * it, or round it; never onto another prop or off the floor. Nothing when no prop stands there.
 */
export function besideProp(props: readonly PropView[], floor: { width: number; depth: number }, x: number, z: number):
  { x: number; z: number } | undefined {
  const prop = propAt(props, x, z);
  if (!prop) {
    return undefined;
  }
  const tapped = Math.hypot(x - prop.x, z - prop.z);
  const ways: [number, number][] = tapped > 0.05 ? [[(x - prop.x) / tapped, (z - prop.z) / tapped]] : [];
  for (let i = 0; i < 8; i++) {
    // The front first (towards the camera), then round both sides to the back.
    const angle = -Math.PI / 2 + Math.ceil(i / 2) * (Math.PI / 4) * (i % 2 === 0 ? -1 : 1);
    ways.push([Math.cos(angle), Math.sin(angle)]);
  }
  const reach = prop.radius + besideGap;
  for (const [dx, dz] of ways) {
    const spot = { x: prop.x + dx * reach, z: prop.z + dz * reach };
    const onFloor = Math.abs(spot.x) <= floor.width / 2 && Math.abs(spot.z) <= floor.depth / 2;
    if (onFloor && props.every((p) => Math.hypot(spot.x - p.x, spot.z - p.z) > p.radius)) {
      return spot;
    }
  }
  return undefined;
}

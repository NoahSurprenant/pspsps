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
} as const;

const box = (width: number, height: number, depth: number, at: [number, number, number], colour: string,
  turn?: [number, number, number]): Part => ({ shape: "box", width, height, depth, at, colour, turn });
const cylinder = (diameter: number, height: number, at: [number, number, number], colour: string,
  extra: { top?: number; sides?: number; turn?: [number, number, number] } = {}): Part =>
  ({ shape: "cylinder", diameter, height, at, colour, top: extra.top, sides: extra.sides, turn: extra.turn });
const ball = (diameter: number, at: [number, number, number], colour: string, height?: number): Part =>
  ({ shape: "ball", diameter, height, at, colour });

/** Each prop's parts at the size it is designed for (<size>: the radius it fits in, outline included). */
const designs: Record<string, { size: number; parts: Part[] }> = {
  // A park bench: wooden slats on dark iron sides, its seat to the front.
  bench: {
    size: 1,
    parts: [
      box(1.6, 0.08, 0.45, [0, 0.45, 0], palette.wood),
      box(1.6, 0.1, 0.06, [0, 0.66, 0.2], palette.wood),
      box(1.6, 0.1, 0.06, [0, 0.84, 0.2], palette.wood),
      box(0.08, 0.45, 0.45, [-0.7, 0.225, 0], palette.iron),
      box(0.08, 0.45, 0.45, [0.7, 0.225, 0], palette.iron),
      box(0.06, 0.5, 0.06, [-0.7, 0.7, 0.2], palette.iron),
      box(0.06, 0.5, 0.06, [0.7, 0.7, 0.2], palette.iron),
    ],
  },
  // A street lamp: a dark post with a pale, glowing lantern under a little roof.
  lamp: {
    size: 0.35,
    parts: [
      cylinder(0.3, 0.2, [0, 0.1, 0], palette.iron),
      cylinder(0.12, 2.5, [0, 1.35, 0], palette.iron, { sides: 8 }),
      box(0.3, 0.36, 0.3, [0, 2.78, 0], palette.glow),
      cylinder(0.44, 0.22, [0, 3.07, 0], palette.iron, { top: 0, sides: 4, turn: [0, Math.PI / 4, 0] }),
    ],
  },
  // A round, leafy tree.
  tree: {
    size: 1.4,
    parts: [
      cylinder(0.36, 1.9, [0, 0.95, 0], palette.darkWood, { top: 0.26, sides: 8 }),
      ball(2.1, [0, 2.45, 0], palette.leaf),
      ball(1.4, [0.45, 3.2, -0.1], palette.lightLeaf),
      ball(1.2, [-0.55, 2.95, 0.25], palette.lightLeaf),
    ],
  },
  // A round stone bed of flowers.
  "flower-bed": {
    size: 0.9,
    parts: [
      cylinder(1.6, 0.26, [0, 0.13, 0], palette.stone, { sides: 20 }),
      cylinder(1.36, 0.06, [0, 0.27, 0], palette.soil, { sides: 20 }),
      ball(0.5, [0, 0.4, 0], palette.lightLeaf, 0.35),
      ...([
        [0.45, 0, palette.pink], [-0.45, 0.05, palette.lilac], [0.1, 0.45, palette.butter], [-0.15, -0.45, palette.white],
        [0.32, -0.32, palette.lilac], [-0.33, 0.33, palette.pink], [0.33, 0.3, palette.white], [-0.35, -0.28, palette.butter],
        [0, 0.05, palette.pink],
      ] as const).flatMap(([x, z, colour]) => [
        ball(0.26, [x, 0.38, z], palette.leaf, 0.16),
        ball(0.2, [x, 0.48, z], colour),
      ]),
    ],
  },
  // A little round café table with a cup on it, and a chair either side.
  "cafe-table": {
    size: 1.2,
    parts: [
      cylinder(0.5, 0.05, [0, 0.025, 0], palette.iron, { sides: 16 }),
      cylinder(0.1, 0.7, [0, 0.38, 0], palette.iron, { sides: 8 }),
      cylinder(0.95, 0.06, [0, 0.74, 0], palette.cream, { sides: 24 }),
      cylinder(0.14, 0.12, [0.15, 0.83, -0.1], palette.white, { sides: 10 }),
      cylinder(0.11, 0.02, [0.15, 0.885, -0.1], palette.darkWood, { sides: 10 }),
      ...[-1, 1].flatMap((side) => [
        box(0.45, 0.06, 0.45, [side * 0.75, 0.45, 0], palette.mustard),
        box(0.06, 0.5, 0.45, [side * 0.95, 0.73, 0], palette.mustard),
        ...[-1, 1].flatMap((front) => [
          box(0.05, 0.45, 0.05, [side * 0.75 + 0.19, 0.225, front * 0.19], palette.iron),
          box(0.05, 0.45, 0.05, [side * 0.75 - 0.19, 0.225, front * 0.19], palette.iron),
        ]),
      ]),
    ],
  },
  // The café's counter: wooden front, cream top, a coffee machine, a till and a cake.
  counter: {
    size: 1.9,
    parts: [
      box(3.2, 1.05, 0.8, [0, 0.525, 0], palette.wood),
      box(3.0, 0.08, 0.04, [0, 0.35, -0.41], palette.darkWood),
      box(3.0, 0.08, 0.04, [0, 0.75, -0.41], palette.darkWood),
      box(3.36, 0.08, 0.92, [0, 1.09, 0], palette.cream),
      box(0.5, 0.6, 0.42, [-1.1, 1.43, 0.12], palette.silver),
      box(0.3, 0.06, 0.14, [-1.1, 1.3, -0.15], palette.iron),
      box(0.42, 0.3, 0.36, [1.15, 1.28, 0.1], palette.teal),
      cylinder(0.5, 0.04, [0.2, 1.15, -0.05], palette.white, { sides: 20 }),
      cylinder(0.4, 0.22, [0.2, 1.28, -0.05], palette.rose, { sides: 20 }),
      cylinder(0.42, 0.05, [0.2, 1.41, -0.05], palette.white, { sides: 20 }),
    ],
  },
  // A potted plant.
  plant: {
    size: 0.5,
    parts: [
      cylinder(0.42, 0.5, [0, 0.25, 0], palette.pot, { top: 0.52, sides: 12 }),
      box(0.5, 0.06, 0.04, [0, 0.36, -0.235], palette.sage, [0.13, 0, 0]),
      ball(0.62, [0, 0.82, 0], palette.leaf),
      ball(0.42, [0.14, 1.1, -0.05], palette.lightLeaf),
      ball(0.36, [-0.15, 1.02, 0.08], palette.lightLeaf),
    ],
  },
  // A cat tree: a carpeted base, two sisal posts, a platform and a round bed at the top, and a toy on a string.
  "cat-tree": {
    size: 0.8,
    parts: [
      box(1.0, 0.1, 1.0, [0, 0.05, 0], palette.lavender),
      cylinder(0.18, 1.0, [-0.25, 0.6, -0.2], palette.sisal, { sides: 10 }),
      box(0.7, 0.08, 0.6, [-0.1, 1.12, -0.1], palette.lavender),
      cylinder(0.18, 0.8, [0.2, 1.55, 0.2], palette.sisal, { sides: 10 }),
      cylinder(0.72, 0.14, [0.1, 2.0, 0.1], palette.lavender, { sides: 16 }),
      box(0.02, 0.4, 0.02, [0.18, 0.88, -0.32], palette.iron),
      ball(0.14, [0.18, 0.64, -0.32], palette.pink),
    ],
  },
  // The furniture players put in their homes, each drawn to fit the circle its shop item takes up (ShopCatalog.cs).
  // A rug, lying flat: a rose border, a cream middle, three lavender stripes across it. Cats walk over it.
  rug: {
    size: 1.3,
    parts: [
      box(1.96, 0.03, 1.2, [0, 0.015, 0], palette.rose),
      box(1.56, 0.036, 0.8, [0, 0.018, 0], palette.cream),
      ...[-0.45, 0, 0.45].map((x): Part => ({ ...box(0.16, 0.04, 0.8, [x, 0.02, 0], palette.lavender), outlined: false })),
    ],
  },
  // An armchair: a mustard frame, its back and arms, and a cream cushion on the seat.
  armchair: {
    size: 0.7,
    parts: [
      box(0.8, 0.32, 0.62, [0, 0.16, 0], palette.mustard),
      box(0.56, 0.12, 0.5, [0, 0.38, -0.04], palette.cream),
      box(0.8, 0.5, 0.15, [0, 0.62, 0.235], palette.mustard),
      ...[-1, 1].map((side) => box(0.13, 0.26, 0.62, [side * 0.335, 0.45, 0], palette.mustard)),
    ],
  },
  // A sofa: a teal frame, its back and arms, two seat cushions and two back cushions.
  sofa: {
    size: 1.2,
    parts: [
      box(1.86, 0.3, 0.76, [0, 0.15, 0], palette.teal),
      box(1.86, 0.56, 0.16, [0, 0.58, 0.3], palette.teal),
      ...[-1, 1].flatMap((side) => [
        box(0.16, 0.5, 0.76, [side * 0.85, 0.25, 0], palette.teal),
        box(0.74, 0.13, 0.56, [side * 0.38, 0.365, -0.07], palette.cream),
        box(0.72, 0.38, 0.11, [side * 0.38, 0.6, 0.165], palette.cream),
      ]),
    ],
  },
  // A floor lamp: a round dark base, a thin pole, a glowing shade.
  "floor-lamp": {
    size: 0.4,
    parts: [
      cylinder(0.4, 0.06, [0, 0.03, 0], palette.iron, { sides: 16 }),
      cylinder(0.06, 1.6, [0, 0.86, 0], palette.iron, { sides: 8 }),
      cylinder(0.46, 0.42, [0, 1.8, 0], palette.glow, { top: 0.28, sides: 16 }),
    ],
  },
  // A bookshelf against nothing in particular: wooden sides, back and shelves, with books in colours on two of them.
  bookshelf: {
    size: 0.9,
    parts: [
      ...[-1, 1].map((side) => box(0.07, 1.7, 0.34, [side * 0.585, 0.85, 0], palette.wood)),
      box(1.24, 1.7, 0.04, [0, 0.85, 0.15], palette.darkWood),
      ...[0.03, 0.6, 1.15, 1.67].map((y) => box(1.1, 0.06, 0.32, [0, y, 0], palette.wood)),
      ...([
        [0.06, [[-0.42, 0.34, palette.teal], [-0.29, 0.4, palette.rose], [-0.16, 0.3, palette.mustard], [0.1, 0.38, palette.sage], [0.24, 0.32, palette.lilac]]],
        [0.63, [[-0.4, 0.36, palette.lilac], [-0.26, 0.3, palette.sage], [0.02, 0.4, palette.teal], [0.16, 0.34, palette.rose], [0.3, 0.38, palette.mustard]]],
      ] as [number, [number, number, string][]][]).flatMap(([shelf, books]) =>
        books.map(([x, height, colour]) => box(0.11, height, 0.24, [x, shelf + height / 2, -0.02], colour))),
    ],
  },
  // A cat's round bed: a lilac basket with a cream cushion in it, and a pink ball.
  "cat-bed": {
    size: 0.6,
    parts: [
      cylinder(0.9, 0.22, [0, 0.11, 0], palette.lilac, { sides: 24 }),
      cylinder(0.68, 0.1, [0, 0.21, 0], palette.cream, { sides: 24 }),
      ball(0.14, [0.18, 0.31, -0.12], palette.pink),
    ],
  },
};

/** The kinds of prop drawn here (the server's PropKinds). */
export const propKinds = Object.keys(designs);

/** A prop's parts, sized to fit its radius, turned to face its way and stood on its spot; none for a kind not drawn here. */
export function propParts(prop: PropView): Part[] {
  const design = designs[prop.kind];
  if (!design) {
    return [];
  }
  // The outline is the same width at any size, so what is drawn inside it scales to leave room for it.
  const scale = Math.max(0, prop.radius - outline) / (design.size - outline);
  const turn = Matrix.RotationY(prop.facing);
  return design.parts.map((part) => {
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

/** How far past a prop's edge a cat sent beside it stands. */
const besideGap = 0.4;

/**
 * Where a tap at (x, z) on a prop (the foot of the point tapped: on a tree's crown, the floor under it) walks the cat:
 * to the floor just beside the prop, on the side tapped, or (tapped in the middle, or that side is taken) in front of
 * it, or round it; never onto another prop or off the floor. Nothing when no prop stands there.
 */
export function besideProp(props: readonly PropView[], floor: { width: number; depth: number }, x: number, z: number):
  { x: number; z: number } | undefined {
  const prop = props.find((p) => Math.hypot(x - p.x, z - p.z) <= p.radius);
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

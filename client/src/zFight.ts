import type { MeshData } from "./meshes";
import { slabs } from "./terraces";
import type { TerraceView } from "./types";

export interface Block {
  name: string;
  colour: string;
  /** Centre then size: x, y, z, width, height, depth. */
  box: [number, number, number, number, number, number];
}

export interface BackdropBox {
  shape: string;
  width?: number;
  height?: number;
  depth?: number;
  diameter?: number;
  at: number[];
  colour?: string;
  turn?: number[];
}

const planeTolerance = 0.002;
const overlapNeeded = 0.02;

/** The axis-aligned boxes among a room's backdrop parts: a turned part, a ball or a cylinder is left out. */
export function blocksOfParts(parts: readonly BackdropBox[]): Block[] {
  return parts.flatMap((part, index) => {
    if (part.shape !== "box" || (part.turn && part.turn.some((angle) => angle !== 0))) {
      return [];
    }
    const [x = 0, y = 0, z = 0] = part.at;
    return [{ name: `part ${index + 1} (box at ${x}, ${z})`, colour: part.colour ?? "#ffffff", box: [x, y, z, part.width ?? 1, part.height ?? 1, part.depth ?? 1] as Block["box"] }];
  });
}

function stoneColour(index: number, count: number): string {
  if (index === count - 1) {
    return "#c9b58e";
  }
  return index % 2 === 0 ? "#e3d8bd" : "#efe6d0";
}

/** The rectangular terraces' slabs, as the client stacks them. */
export function blocksOfTerraces(terraces: readonly TerraceView[]): Block[] {
  return terraces.flatMap((terrace, t) => {
    if (terrace.round || terrace.slope) {
      return [];
    }
    return slabs(terrace).map((slab, index, all) => ({
      name: `terrace ${t + 1} step ${index + 1}`,
      colour: slab.colour ?? stoneColour(index, all.length),
      box: [slab.x, slab.top / 2, slab.z, slab.width, slab.top, slab.depth] as Block["box"],
    }));
  });
}

type Box = Block["box"];

const faceNames = [["west", "east"], ["bottom", "top"], ["south", "north"]] as const;

const low = (box: Box, axis: number): number => (box[axis] ?? 0) - (box[axis + 3] ?? 0) / 2;
const high = (box: Box, axis: number): number => (box[axis] ?? 0) + (box[axis + 3] ?? 0) / 2;

/** Whether two boxes overlap across the two axes other than this one, by more than a sliver. */
function overlapAcross(p: Box, q: Box, axis: number): boolean {
  return [0, 1, 2].filter((i) => i !== axis).every((i) => Math.min(high(p, i), high(q, i)) - Math.max(low(p, i), low(q, i)) > overlapNeeded);
}

/** The faces, on one axis, that two boxes show in one plane: [side, height of the plane]. A bottom face is hidden by whatever it stands on. */
function sharedFaces(p: Box, q: Box, axis: number): { name: string; at: number }[] {
  const shared: { name: string; at: number }[] = [];
  if (overlapAcross(p, q, axis)) {
    for (const side of [0, 1]) {
      const [planeP, planeQ] = side === 0 ? [low(p, axis), low(q, axis)] : [high(p, axis), high(q, axis)];
      if (Math.abs(planeP - planeQ) < planeTolerance && !(axis === 1 && side === 0)) {
        shared.push({ name: faceNames[axis]?.[side] ?? "", at: planeP });
      }
    }
  }
  return shared;
}

/** Pairs of faces that lie in one plane, face the same way, overlap, and are painted differently: a camera sees them flicker (z-fighting). A bottom face is hidden and left out. */
export function zFights(blocks: readonly Block[]): string[] {
  const found: string[] = [];
  blocks.forEach((a, index) => {
    for (const b of blocks.slice(index + 1)) {
      if (a.colour === b.colour) {
        continue;
      }
      for (const axis of [0, 1, 2]) {
        for (const face of sharedFaces(a.box, b.box, axis)) {
          found.push(`${a.name} and ${b.name} share a ${face.name} face at ${face.at.toFixed(2)}`);
        }
      }
    }
  });
  return found;
}

type V3 = [number, number, number];

interface Triangle {
  corners: [V3, V3, V3];
  normal: [number, number, number];
  offset: number;
  colour: string;
  area: number;
}

const sameDirection = 0.9995;
const overlapArea = 0.0005;

function trianglesOf(mesh: MeshData): Triangle[] {
  const point = (index: number): V3 => [mesh.positions[index * 3] ?? 0, mesh.positions[index * 3 + 1] ?? 0, mesh.positions[index * 3 + 2] ?? 0];
  const found: Triangle[] = [];
  for (let i = 0; i < mesh.indices.length; i += 3) {
    const [a, b, c] = [point(mesh.indices[i] ?? 0), point(mesh.indices[i + 1] ?? 0), point(mesh.indices[i + 2] ?? 0)];
    const u: V3 = [b[0] - a[0], b[1] - a[1], b[2] - a[2]];
    const v: V3 = [c[0] - a[0], c[1] - a[1], c[2] - a[2]];
    const cross: [number, number, number] = [u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0]];
    const length = Math.hypot(...cross);
    if (length > 1e-9) {
      const normal: [number, number, number] = [cross[0] / length, cross[1] / length, cross[2] / length];
      found.push({ corners: [a, b, c], normal, offset: normal[0] * a[0] + normal[1] * a[1] + normal[2] * a[2], colour: mesh.colours[mesh.indices[i] ?? 0] ?? "", area: length / 2 });
    }
  }
  return found;
}

/** Sutherland-Hodgman: the polygon clipped to one edge of a counter-clockwise triangle, in two dimensions. */
function clip(polygon: [number, number][], from: [number, number], to: [number, number]): [number, number][] {
  const side = (p: [number, number]): number => (to[0] - from[0]) * (p[1] - from[1]) - (to[1] - from[1]) * (p[0] - from[0]);
  const out: [number, number][] = [];
  polygon.forEach((p, index) => {
    const q = polygon[(index + 1) % polygon.length] ?? p;
    const [sp, sq] = [side(p), side(q)];
    if (sp >= 0) {
      out.push(p);
    }
    if (sp >= 0 !== sq >= 0) {
      const t = sp / (sp - sq);
      out.push([p[0] + t * (q[0] - p[0]), p[1] + t * (q[1] - p[1])]);
    }
  });
  return out;
}

function areaOf(polygon: [number, number][]): number {
  return Math.abs(polygon.reduce((sum, p, i) => { const q = polygon[(i + 1) % polygon.length] ?? p; return sum + p[0] * q[1] - q[0] * p[1]; }, 0)) / 2;
}

/** How much two triangles in one plane overlap, in square metres, seen along the plane's strongest axis. */
function overlapOf(p: Triangle, q: Triangle): number {
  const axis = p.normal.map(Math.abs).indexOf(Math.max(...p.normal.map(Math.abs)));
  const flat = (t: Triangle): [number, number][] => {
    const flatten = (c: V3): [number, number] => {
      if (axis === 0) return [c[1], c[2]];
      return axis === 1 ? [c[0], c[2]] : [c[0], c[1]];
    };
    const [a, b, c] = t.corners.map(flatten) as [[number, number], [number, number], [number, number]];
    const turn = (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0]);
    return turn < 0 ? [c, b, a] : [a, b, c];
  };
  const [subject, other] = [flat(p), flat(q)];
  let polygon = subject;
  other.forEach((from, i) => {
    polygon = polygon.length > 0 ? clip(polygon, from, other[(i + 1) % 3] ?? from) : polygon;
  });
  return polygon.length >= 3 ? areaOf(polygon) : 0;
}

/** A model's triangles that lie in one plane, face the same way, overlap, and are painted differently, one line for each pair of colours and plane. */
export function meshFights(mesh: MeshData): string[] {
  const triangles = trianglesOf(mesh);
  const found = new Set<string>();
  triangles.forEach((p, index) => {
    for (const q of triangles.slice(index + 1)) {
      const sameColour = p.colour === q.colour;
      const dot = p.normal[0] * q.normal[0] + p.normal[1] * q.normal[1] + p.normal[2] * q.normal[2];
      const hidden = p.normal[1] < -0.5;
      if (!hidden && !sameColour && dot > sameDirection && Math.abs(p.offset - q.offset) < planeTolerance && overlapOf(p, q) > overlapArea) {
        const middle = p.corners.reduce((sum, c) => [sum[0] + c[0] / 3, sum[1] + c[1] / 3, sum[2] + c[2] / 3], [0, 0, 0]);
        found.add(`${[p.colour, q.colour].sort().join(" over ")} facing ${p.normal.map((n) => n.toFixed(1)).join(",")} at ${p.offset.toFixed(2)}, near ${middle.map((n) => n.toFixed(1)).join(", ")}`);
      }
    }
  });
  return [...found];
}

// The rooms' painted surfaces (floors and back walls), drawn in code onto a canvas in the game's flat, illustrated style:
// flat colours, the dark ink for edges. Nothing to download: each is painted on the way into its room.
import { ink, palette } from "./props";
import type { DoorView, PondView } from "./types";

/** What a painter needs to know of the room: its size, and where its pond and doors are. */
export interface Floorplan {
  width: number;
  depth: number;
  pond: PondView | null;
  doors: readonly DoorView[];
}

/** A canvas's 2D context: a real one in the browser; a stand-in that draws nothing in the unit tests. */
export type Brush = CanvasRenderingContext2D | OffscreenCanvasRenderingContext2D;

/** How many pixels a metre of floor is painted at, and a metre of wall (seen closer, and with writing on it). */
export const floorPixelsPerMetre = 64;
export const wallPixelsPerMetre = 80;
/** The most pixels a painted surface is across, whatever the room's size. */
export const largestTexture = 2048;

/** How many pixels to paint a surface <width> by <height> metres at: a metre the same everywhere on it. */
export function textureSize(width: number, height: number, perMetre: number): { width: number; height: number; scale: number } {
  const scale = Math.min(perMetre, largestTexture / Math.max(width, height));
  return { width: Math.round(width * scale), height: Math.round(height * scale), scale };
}

/**
 * Where a spot on the floor is painted, in pixels from the texture's top left. The top of the texture is the back of
 * the room (+Z, by the back wall) and its left the room's left (-X), as the camera sees it.
 */
export function floorPixel(plan: Pick<Floorplan, "width" | "depth">, scale: number, x: number, z: number): [number, number] {
  return [(x + plan.width / 2) * scale, (plan.depth / 2 - z) * scale];
}

/** A painter's random numbers, the same every time for the same seed (so a room looks the same on every visit). */
export function seeded(seed: number): () => number {
  let state = seed >>> 0;
  return () => {
    state = (state + 0x6d2b79f5) >>> 0;
    let t = state;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

// Colours only the painted surfaces use. Like the props', none is the pond's blue, a door mat's red or a cat's eyes.
export const paints = {
  grass: "#9fd38a",
  darkGrass: "#86c172",
  lightGrass: "#b7e0a2",
  paving: "#ece2cb",
  paverJoint: "#d6c7a5",
  pavingEdge: "#bfae8a",
  sand: "#e8d9ae",
  pebble: "#cdbb8f",
  street: "#d9ccb2",
  streetLight: "#e1d6bf",
  streetJoint: "#c8b896",
  plank: "#d9b38c",
  plankLight: "#dfbd98",
  plankDark: "#cfa77f",
  seam: "#b48a63",
  rug: "#5f9e96",
  rugDark: "#4c857e",
  cafeWall: "#f2d6c2",
  cafeStripe: "#f7e3d3",
  chalkboard: "#3d4a45",
  chalk: "#eef2ea",
  glass: "#dceff2",
  windowSky: "#cfe8f0",
  plinth: "#cfc2a8",
  panelLine: "#a8724a",
} as const;

/**
 * The town's houses, left to right: each a shopfront in the square's back wall and, above and behind it, the rest of
 * the house (a backdrop for the fixed camera). <from> and <to> are along X, in metres; the ones past the square's ends
 * stand on the street beside it.
 */
export const houses: readonly { from: number; to: number; height: number; facade: string; roof: string }[] = [
  { from: -30, to: -24.5, height: 6.2, facade: "#cfe0c3", roof: "#6f7f99" },
  { from: -24.5, to: -19, height: 7.2, facade: "#f2c9a0", roof: "#8a6f8f" },
  { from: -19, to: -14, height: 5.8, facade: "#d9c7e8", roof: "#4f7f86" },
  { from: -14, to: -8.5, height: 5.8, facade: "#f6e3b4", roof: "#6f7f99" },
  { from: -8.5, to: -3, height: 7.2, facade: "#cfe0c3", roof: "#9a6a4f" },
  { from: -3, to: 2.5, height: 6.2, facade: "#f4d0d0", roof: "#4f7f86" },
  // The café's: its door is the door in the wall.
  { from: 2.5, to: 9.5, height: 6.8, facade: "#f2c9a0", roof: "#8a6f8f" },
  { from: 9.5, to: 14, height: 5.8, facade: "#d9c7e8", roof: "#6f7f99" },
  { from: 14, to: 19.5, height: 6.8, facade: "#f6e3b4", roof: "#9a6a4f" },
  { from: 19.5, to: 25, height: 6.2, facade: "#cfe0c3", roof: "#8a6f8f" },
  { from: 25, to: 30, height: 7.2, facade: "#f4d0d0", roof: "#4f7f86" },
];

/** Town Square's floor: grass, a paved plaza round <plaza>, stepping stones from it to the doors and the pond, sand round the pond. */
export function paintTownSquareFloor(brush: Brush, plan: Floorplan, scale: number, plaza: { x: number; z: number }): void {
  const at = (x: number, z: number) => floorPixel(plan, scale, x, z);
  const random = seeded(16);
  brush.fillStyle = paints.grass;
  brush.fillRect(0, 0, plan.width * scale, plan.depth * scale);
  // Tufts of grass, and a few lighter specks.
  brush.lineWidth = Math.max(1.5, scale * 0.05);
  brush.lineCap = "round";
  brush.strokeStyle = paints.darkGrass;
  for (let i = 0; i < plan.width * plan.depth * 1.2; i++) {
    const [x, y] = [random() * plan.width * scale, random() * plan.depth * scale];
    const size = scale * (0.1 + random() * 0.08);
    brush.beginPath();
    brush.moveTo(x - size, y - size);
    brush.lineTo(x, y);
    brush.lineTo(x + size * 0.8, y - size * 1.2);
    brush.stroke();
  }
  brush.fillStyle = paints.lightGrass;
  for (let i = 0; i < plan.width * plan.depth * 0.5; i++) {
    brush.beginPath();
    brush.arc(random() * plan.width * scale, random() * plan.depth * scale, scale * 0.05, 0, Math.PI * 2);
    brush.fill();
  }

  // Stepping stones from the plaza to each door, and to the pond's sand.
  for (const door of plan.doors) {
    steppingStones(brush, at, plaza, door, 0);
  }
  if (plan.pond) {
    steppingStones(brush, at, plaza, plan.pond, plan.pond.radius + 0.6);
  }

  // The plaza: a paved oval, its pavers laid like bricks.
  const [cx, cy] = at(plaza.x, plaza.z);
  const [rx, ry] = [5.5 * scale, 3 * scale];
  brush.save();
  brush.beginPath();
  brush.ellipse(cx, cy, rx, ry, 0, 0, Math.PI * 2);
  brush.fillStyle = paints.paving;
  brush.fill();
  brush.clip();
  brush.strokeStyle = paints.paverJoint;
  brush.lineWidth = Math.max(1, scale * 0.04);
  const paver = 0.6 * scale;
  for (let row = 0, y = cy - ry; y < cy + ry; row++, y += paver) {
    brush.beginPath();
    brush.moveTo(cx - rx, y);
    brush.lineTo(cx + rx, y);
    for (let x = cx - rx + (row % 2) * paver; x < cx + rx; x += paver * 1.5) {
      brush.moveTo(x, y);
      brush.lineTo(x, y + paver);
    }
    brush.stroke();
  }
  brush.restore();
  brush.beginPath();
  brush.ellipse(cx, cy, rx, ry, 0, 0, Math.PI * 2);
  brush.strokeStyle = paints.pavingEdge;
  brush.lineWidth = Math.max(2, scale * 0.08);
  brush.stroke();

  // Sand and pebbles round the pond (the pond's water and its dark edge are drawn over it, in the scene).
  if (plan.pond) {
    const [px, py] = at(plan.pond.x, plan.pond.z);
    brush.beginPath();
    brush.arc(px, py, (plan.pond.radius + 0.6) * scale, 0, Math.PI * 2);
    brush.fillStyle = paints.sand;
    brush.fill();
    brush.fillStyle = paints.pebble;
    for (let i = 0; i < 36; i++) {
      const angle = (i / 36) * Math.PI * 2 + random() * 0.1;
      const distance = (plan.pond.radius + 0.25 + random() * 0.25) * scale;
      brush.beginPath();
      brush.arc(px + Math.cos(angle) * distance, py + Math.sin(angle) * distance, scale * (0.05 + random() * 0.05), 0, Math.PI * 2);
      brush.fill();
    }
  }
}

/** Flat stones every so often along the straight walk from <from> to <to>, stopping <short> metres before it. */
function steppingStones(brush: Brush, at: (x: number, z: number) => [number, number], from: { x: number; z: number },
  to: { x: number; z: number }, short: number): void {
  const length = Math.hypot(to.x - from.x, to.z - from.z) - short;
  const [ux, uz] = [(to.x - from.x) / (length + short), (to.z - from.z) / (length + short)];
  const [ax, ay] = at(from.x, from.z);
  const [bx, by] = at(from.x + ux, from.z + uz);
  const angle = Math.atan2(by - ay, bx - ax);
  const scale = Math.hypot(bx - ax, by - ay);
  for (let along = 0.4; along < length - 0.3; along += 0.9) {
    const [x, y] = at(from.x + ux * along, from.z + uz * along);
    brush.save();
    brush.translate(x, y);
    brush.rotate(angle);
    brush.beginPath();
    brush.roundRect(-0.3 * scale, -0.42 * scale, 0.6 * scale, 0.84 * scale, 0.18 * scale);
    brush.fillStyle = paints.paving;
    brush.fill();
    brush.strokeStyle = paints.pavingEdge;
    brush.lineWidth = Math.max(1.5, scale * 0.06);
    brush.stroke();
    brush.restore();
  }
}

/**
 * How far a shop window's middle keeps from a door's: half its awning (0.95 m) and half the door's frame (0.95 m), so
 * neither runs into the other.
 */
const windowClearOfDoor = 1.9;

/** The square's back wall: the ground floors of the houses behind it, shopfronts with striped awnings. */
export function paintTownSquareWall(brush: Brush, width: number, height: number, scale: number, doors: readonly DoorView[]): void {
  const x = (metres: number) => (metres + width / 2) * scale;
  const y = (metres: number) => (height - metres) * scale;
  for (const house of houses) {
    const [left, right] = [Math.max(house.from, -width / 2), Math.min(house.to, width / 2)];
    if (right <= left) {
      continue;
    }
    brush.fillStyle = house.facade;
    brush.fillRect(x(left), 0, (right - left) * scale, height * scale);
    // Shop windows, spread along the front, but never where a door is.
    const windows = Math.max(1, Math.round((house.to - house.from) / 2.8));
    const step = (house.to - house.from) / windows;
    for (let i = 0; i < windows; i++) {
      const middle = house.from + step * (i + 0.5);
      if (middle < -width / 2 + 0.8 || middle > width / 2 - 0.8 || doors.some((door) => Math.abs(door.x - middle) < windowClearOfDoor)) {
        continue;
      }
      shopWindow(brush, x(middle), y, scale, house.roof);
    }
    // An awning over each door in this house.
    for (const door of doors.filter((d) => d.x > house.from && d.x < house.to)) {
      awning(brush, x(door.x), y(3.15), 2.4 * scale, scale, palette.mustard);
    }
  }
  // A stone plinth along the bottom, and the line between houses.
  brush.fillStyle = paints.plinth;
  brush.fillRect(0, y(0.35), width * scale, 0.35 * scale);
  brush.strokeStyle = ink;
  brush.lineWidth = Math.max(2, scale * 0.06);
  brush.beginPath();
  brush.moveTo(0, y(0.35));
  brush.lineTo(width * scale, y(0.35));
  for (const house of houses) {
    if (house.from > -width / 2 && house.from < width / 2) {
      brush.moveTo(x(house.from), 0);
      brush.lineTo(x(house.from), height * scale);
    }
  }
  brush.stroke();
}

function shopWindow(brush: Brush, middle: number, y: (metres: number) => number, scale: number, trim: string): void {
  const [w, top, bottom] = [1.6 * scale, y(2.55), y(0.75)];
  brush.fillStyle = paints.glass;
  brush.fillRect(middle - w / 2, top, w, bottom - top);
  // Something on show: a few round shapes on a shelf.
  brush.fillStyle = trim;
  brush.fillRect(middle - w / 2, y(1.15), w, 0.08 * scale);
  for (const [dx, r] of [[-0.45, 0.18], [0, 0.24], [0.45, 0.16]] as const) {
    brush.beginPath();
    brush.arc(middle + dx * scale, y(1.15) - r * scale, r * scale, 0, Math.PI * 2);
    brush.fill();
  }
  brush.strokeStyle = ink;
  brush.lineWidth = Math.max(2, scale * 0.07);
  brush.strokeRect(middle - w / 2, top, w, bottom - top);
  brush.beginPath();
  brush.moveTo(middle, top);
  brush.lineTo(middle, y(1.15));
  brush.stroke();
  awning(brush, middle, y(2.95), w + 0.3 * scale, scale, trim);
}

/** A striped awning, <w> wide, its top edge at <top>. */
function awning(brush: Brush, middle: number, top: number, w: number, scale: number, colour: string): void {
  const [drop, stripes] = [0.45 * scale, 6];
  for (let i = 0; i < stripes; i++) {
    brush.fillStyle = i % 2 === 0 ? colour : palette.white;
    brush.fillRect(middle - w / 2 + (w / stripes) * i, top, w / stripes, drop);
  }
  // A scalloped edge.
  for (let i = 0; i < stripes; i++) {
    brush.fillStyle = i % 2 === 0 ? colour : palette.white;
    brush.beginPath();
    brush.arc(middle - w / 2 + (w / stripes) * (i + 0.5), top + drop, w / stripes / 2, 0, Math.PI);
    brush.fill();
  }
  brush.strokeStyle = ink;
  brush.lineWidth = Math.max(2, scale * 0.05);
  brush.strokeRect(middle - w / 2, top, w, drop);
}

/** The café's floor: honey-coloured planks, and a teal rug where the tables are. */
export function paintCafeFloor(brush: Brush, plan: Floorplan, scale: number): void {
  const random = seeded(18);
  const [w, h] = [plan.width * scale, plan.depth * scale];
  const plank = 0.42 * scale;
  const shades = [paints.plank, paints.plankLight, paints.plankDark] as const;
  brush.lineWidth = Math.max(1.5, scale * 0.04);
  brush.strokeStyle = paints.seam;
  for (let y = 0; y < h; y += plank) {
    for (let x = -random() * 2 * scale; x < w; ) {
      const length = (1.4 + random() * 1.6) * scale;
      brush.fillStyle = pick(shades, random());
      brush.fillRect(x, y, length, plank);
      brush.strokeRect(x, y, length, plank);
      x += length;
    }
  }

  // The rug, under the tables: a rounded teal rectangle with a cream border and a row of mustard diamonds.
  const [cx, cy] = floorPixel(plan, scale, 0, -1.6);
  const [rw, rh] = [11 * scale, 5 * scale];
  brush.beginPath();
  brush.roundRect(cx - rw / 2, cy - rh / 2, rw, rh, 0.6 * scale);
  brush.fillStyle = palette.cream;
  brush.fill();
  brush.strokeStyle = ink;
  brush.lineWidth = Math.max(2, scale * 0.06);
  brush.stroke();
  const border = 0.3 * scale;
  brush.beginPath();
  brush.roundRect(cx - rw / 2 + border, cy - rh / 2 + border, rw - border * 2, rh - border * 2, 0.4 * scale);
  brush.fillStyle = paints.rug;
  brush.fill();
  brush.fillStyle = palette.mustard;
  for (const row of [-1, 1]) {
    for (let x = cx - rw / 2 + border * 3; x < cx + rw / 2 - border * 2; x += 0.9 * scale) {
      const y = cy + row * (rh / 2 - border * 2.2);
      brush.beginPath();
      brush.moveTo(x, y - 0.2 * scale);
      brush.lineTo(x + 0.2 * scale, y);
      brush.lineTo(x, y + 0.2 * scale);
      brush.lineTo(x - 0.2 * scale, y);
      brush.closePath();
      brush.fill();
    }
  }
  brush.strokeStyle = paints.rugDark;
  brush.lineWidth = Math.max(2, scale * 0.08);
  brush.beginPath();
  brush.roundRect(cx - rw / 2 + border * 4, cy - rh / 2 + border * 4, rw - border * 8, rh - border * 8, 0.3 * scale);
  brush.stroke();
}

/** The café's back wall: striped wallpaper over wooden panelling, a chalkboard menu, shelves of cups, a window. */
export function paintCafeWall(brush: Brush, width: number, height: number, scale: number, doors: readonly DoorView[]): void {
  const x = (metres: number) => (metres + width / 2) * scale;
  const y = (metres: number) => (height - metres) * scale;
  brush.fillStyle = paints.cafeWall;
  brush.fillRect(0, 0, width * scale, height * scale);
  brush.fillStyle = paints.cafeStripe;
  for (let m = -width / 2; m < width / 2; m += 0.6) {
    brush.fillRect(x(m), 0, 0.25 * scale, height * scale);
  }
  // Panelling, waist high.
  brush.fillStyle = palette.wood;
  brush.fillRect(0, y(1.1), width * scale, 1.1 * scale);
  brush.strokeStyle = paints.panelLine;
  brush.lineWidth = Math.max(2, scale * 0.04);
  for (let m = -width / 2 + 0.15; m < width / 2 - 0.9; m += 1.0) {
    brush.strokeRect(x(m), y(0.95), 0.8 * scale, 0.8 * scale);
  }
  brush.fillStyle = palette.darkWood;
  brush.fillRect(0, y(1.18), width * scale, 0.1 * scale);

  const clear = (from: number, to: number) => !doors.some((door) => door.x + 1 > from && door.x - 1 < to);
  // A window onto the street.
  if (clear(-6.6, -5.2)) {
    frame(brush, x(-6.6), y(3.3), 1.4 * scale, 1.8 * scale, scale, paints.windowSky);
    brush.fillStyle = palette.lightLeaf;
    brush.fillRect(x(-6.6), y(1.95), 1.4 * scale, 0.45 * scale);
    brush.strokeStyle = ink;
    brush.lineWidth = Math.max(2, scale * 0.05);
    brush.beginPath();
    brush.moveTo(x(-5.9), y(3.3));
    brush.lineTo(x(-5.9), y(1.5));
    brush.moveTo(x(-6.6), y(2.4));
    brush.lineTo(x(-5.2), y(2.4));
    brush.stroke();
  }
  // The menu: a chalkboard with a fish and a cup, and lines of chalk writing.
  if (clear(-1.8, 0.6)) {
    frame(brush, x(-1.8), y(3.4), 2.4 * scale, 1.75 * scale, scale, paints.chalkboard);
    brush.strokeStyle = paints.chalk;
    brush.fillStyle = paints.chalk;
    brush.lineWidth = Math.max(1.5, scale * 0.03);
    brush.font = `bold ${Math.round(0.3 * scale)}px sans-serif`;
    brush.textAlign = "center";
    brush.fillText("Menu", x(-0.6), y(3.0));
    for (let line = 0; line < 3; line++) {
      brush.beginPath();
      brush.moveTo(x(-1.5), y(2.6 - line * 0.32));
      brush.lineTo(x(-0.35 - (line % 2) * 0.3), y(2.6 - line * 0.32));
      brush.stroke();
    }
    // A fish.
    brush.beginPath();
    brush.ellipse(x(0.05), y(2.35), 0.28 * scale, 0.14 * scale, 0, 0, Math.PI * 2);
    brush.moveTo(x(0.33), y(2.35));
    brush.lineTo(x(0.48), y(2.47));
    brush.lineTo(x(0.48), y(2.23));
    brush.closePath();
    brush.stroke();
  }
  // Shelves of cups and jars above the counter.
  if (clear(2.6, 6.4)) {
    const cups = [palette.white, palette.teal, palette.mustard, palette.rose, palette.sage];
    for (const [shelf, count] of [[2.3, 6], [3.1, 5]] as const) {
      brush.fillStyle = palette.darkWood;
      brush.fillRect(x(2.7), y(shelf), 3.6 * scale, 0.08 * scale);
      for (let i = 0; i < count; i++) {
        const left = 2.9 + i * (3.2 / count);
        brush.fillStyle = cups[(i + count) % cups.length] ?? palette.white;
        brush.fillRect(x(left), y(shelf + 0.32), 0.28 * scale, 0.32 * scale);
        brush.strokeStyle = ink;
        brush.lineWidth = Math.max(1.5, scale * 0.03);
        brush.strokeRect(x(left), y(shelf + 0.32), 0.28 * scale, 0.32 * scale);
      }
    }
  }
  // A framed picture of a fish.
  if (clear(1.2, 2.2)) {
    frame(brush, x(1.2), y(3.5), 1.0 * scale, 0.8 * scale, scale, paints.windowSky);
    brush.fillStyle = palette.mustard;
    brush.beginPath();
    brush.ellipse(x(1.65), y(3.1), 0.25 * scale, 0.13 * scale, 0, 0, Math.PI * 2);
    brush.fill();
  }
  // The line under the ceiling.
  brush.fillStyle = palette.darkWood;
  brush.fillRect(0, 0, width * scale, 0.12 * scale);
}

function frame(brush: Brush, left: number, top: number, w: number, h: number, scale: number, inside: string): void {
  brush.fillStyle = inside;
  brush.fillRect(left, top, w, h);
  brush.strokeStyle = ink;
  brush.lineWidth = Math.max(2, scale * 0.08);
  brush.strokeRect(left, top, w, h);
}

/** One of <choices>, by <roll> (0 to 1). */
function pick<T>(choices: readonly [T, ...T[]], roll: number): T {
  return choices[Math.floor(roll * choices.length)] ?? choices[0];
}

/** How many metres of street one painting of its cobbles covers (it repeats). */
export const streetTile = 2;

/** One tile of the town's cobbled street, <scale> pixels to the metre: rounded stones in rows, a little apart. */
export function paintStreet(brush: Brush, scale: number): void {
  const size = streetTile * scale;
  const random = seeded(7);
  brush.fillStyle = paints.streetJoint;
  brush.fillRect(0, 0, size, size);
  const rows = 4;
  const height = size / rows;
  for (let row = 0; row < rows; row++) {
    const across = 4;
    const width = size / across;
    for (let i = -1; i < across; i++) {
      const left = i * width + (row % 2) * width / 2;
      brush.fillStyle = random() < 0.3 ? paints.streetLight : paints.street;
      brush.beginPath();
      const gap = scale * 0.03;
      brush.roundRect(left + gap, row * height + gap, width - gap * 2, height - gap * 2, height / 5);
      brush.fill();
    }
  }
}

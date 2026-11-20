// The rooms' painted surfaces (floors and back walls), drawn in code onto a canvas in the game's flat, illustrated style:
// flat colours, the dark ink for edges. Nothing to download: each is painted on the way into its room.
import { ink, palette } from "./props";
import type { DoorView, PondView, PropView, WorldMapView } from "./types";

/** What a painter needs to know of the room: its size, and where its pond and doors are. */
export interface Floorplan {
  width: number;
  depth: number;
  pond: PondView | null;
  doors: readonly DoorView[];
  props?: readonly PropView[] | null;
  worldMap?: WorldMapView | null;
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
  homeBoard: "#e2bf98",
  homeBoardLight: "#e8c9a6",
  homeBoardDark: "#d7b18a",
  homeSeam: "#bd946c",
  wallpaper: "#f5e4d6",
  wallpaperSprig: "#e3c6b4",
  skirting: "#fbf3ea",
  curtain: "#d98ba6",
  shopTile: "#cfe6d4",
  shopTileLight: "#f4ecdc",
  shopGrout: "#b9cfbf",
  shopRug: "#c9b3e0",
  shopRugDark: "#a98fc8",
  shopWall: "#f7ecc9",
  shopStripe: "#f0dfae",
} as const;

/**
 * The town's houses, left to right: each a shopfront in the square's back wall and, above and behind it, the rest of
 * the house (a backdrop for the fixed camera). <from> and <to> are along X, in metres; the ones past the square's ends
 * stand on the street beside it.
 */
const scenery: readonly { from: number; to: number; depth?: number; height: number; facade: string; roof: string }[] = [
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

/** The shop and café occupy the same land seen from the square as they do on the map. */
export function townHouses(map?: WorldMapView | null) {
  const square = map?.rooms.find(room => room.id === "town-square");
  if (!map || !square) return scenery;
  const storefronts = map.rooms.filter(room => room.id === "shop" || room.id === "cafe");
  // Older snapshots placed the building markers at a distance from the square.
  if (storefronts.some(room => Math.abs(room.z - room.depth / 2 - square.z - square.depth / 2) > 0.000001)) return scenery;
  const buildings = storefronts.map(room => ({
    from: room.x - square.x - room.width / 2,
    to: room.x - square.x + room.width / 2,
    depth: room.depth,
    height: room.id === "shop" ? 6.2 : 6.8,
    facade: room.id === "shop" ? "#f4d0d0" : "#f2c9a0",
    roof: room.id === "shop" ? "#4f7f86" : "#8a6f8f",
  }));
  return [...scenery.filter(house => buildings.every(b => house.to <= b.from || house.from >= b.to)), ...buildings]
    .sort((a, b) => a.from - b.from);
}

/** Town Square's floor: grass, a paved plaza round <plaza>, stepping stones from it to the doors and the pond, sand round the pond. */
export function paintTownSquareFloor(brush: Brush, plan: Floorplan, scale: number, plaza: { x: number; z: number }): void {
  const at = (x: number, z: number) => floorPixel(plan, scale, x, z);
  const random = seeded(16);
  paintGrass(brush, plan, scale, random);
  for (const door of plan.doors) {
    steppingStones(brush, at, plaza, door, 0);
  }
  if (plan.pond) {
    steppingStones(brush, at, plaza, plan.pond, plan.pond.radius + 0.6);
  }
  paintPlaza(brush, at(plaza.x, plaza.z), scale);
  paintPondBank(brush, plan, scale, random);
}

function paintGrass(brush: Brush, plan: Floorplan, scale: number, random: () => number): void {
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

}

function paintPlaza(brush: Brush, [cx, cy]: [number, number], scale: number): void {
  // The plaza: a paved oval, its pavers laid like bricks.
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
}

function paintPondBank(brush: Brush, plan: Floorplan, scale: number, random: () => number): void {
  // Sand and pebbles round the pond (the pond's water and its dark edge are drawn over it, in the scene).
  if (plan.pond) {
    const [px, py] = floorPixel(plan, scale, plan.pond.x, plan.pond.z);
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

export function paintGardenFloor(brush: Brush, plan: Floorplan, scale: number): void {
  paintGrass(brush, plan, scale, seeded(32));
  const at = (x: number, z: number) => floorPixel(plan, scale, x, z);
  for (const door of plan.doors) {
    steppingStones(brush, at, { x: 0, z: 0.5 }, door, 0);
  }
  paintPlaza(brush, at(0, -1.6), scale);
}

export function paintOrchardFloor(brush: Brush, plan: Floorplan, scale: number): void {
  paintGrass(brush, plan, scale, seeded(51));
  const at = (x: number, z: number) => floorPixel(plan, scale, x, z);
  const props = plan.props ?? [];
  const crate = props.find(prop => prop.kind === "packing-crate");
  if (!crate) return;
  // The barrow leaves two wheel tracks between the packing station and each gate.
  for (const door of plan.doors) {
    for (const offset of [-0.32, 0.32]) {
      brush.strokeStyle = "#c8b988";
      brush.lineWidth = 0.19 * scale;
      brush.beginPath();
      brush.moveTo(...at(crate.x + offset, crate.z));
      brush.quadraticCurveTo(...at(crate.x + offset, door.z), ...at(door.x + offset, door.z));
      brush.stroke();
    }
  }
  for (const tree of props.filter(prop => prop.kind === "pear-tree")) {
    brush.fillStyle = "#b6a277";
    brush.beginPath();
    brush.arc(...at(tree.x, tree.z), 1.2 * scale, 0, 2 * Math.PI);
    brush.fill();
  }
}

export function paintRiversideFloor(brush: Brush, plan: Floorplan, scale: number): void {
  const random = seeded(69);
  paintGrass(brush, plan, scale, random);
  const at = (x: number, z: number) => floorPixel(plan, scale, x, z);
  brush.lineWidth = 1.65 * scale;
  brush.strokeStyle = paints.sand;
  for (const door of plan.doors) {
    const [x, y] = at(door.x, door.z);
    const [bendX, bendY] = at(door.x * 0.75, -0.25);
    const [meetX, meetY] = at(0, 1.4);
    brush.beginPath();
    brush.moveTo(x, y);
    brush.quadraticCurveTo(bendX, bendY, meetX, meetY);
    brush.stroke();
  }
  if (plan.pond) {
    steppingStones(brush, at, { x: 0, z: 1.4 }, plan.pond, plan.pond.radius + 0.6);
  }
  paintPondBank(brush, plan, scale, random);
}

export function paintGardenWall(brush: Brush, width: number, height: number, scale: number): void {
  brush.fillStyle = "#f0ddba";
  brush.fillRect(0, 0, width * scale, height * scale);
  brush.strokeStyle = "#ceb48d";
  brush.lineWidth = Math.max(2, scale * 0.04);
  for (let row = 0; row < height / 0.55; row++) {
    const y = row * 0.55 * scale;
    brush.beginPath();
    brush.moveTo(0, y);
    brush.lineTo(width * scale, y);
    for (let x = (row % 2) * 0.75; x < width; x += 1.5) {
      brush.moveTo(x * scale, y);
      brush.lineTo(x * scale, Math.min(height * scale, y + 0.55 * scale));
    }
    brush.stroke();
  }
}

export function paintRiversideWall(brush: Brush, width: number, height: number, scale: number): void {
  brush.fillStyle = "#ac805b";
  brush.fillRect(0, 0, width * scale, height * scale);
  brush.fillStyle = "#97704f";
  for (let x = 0; x < width; x += 0.65) {
    brush.fillRect(x * scale, 0, 0.05 * scale, height * scale);
  }
  brush.fillStyle = "#d1ab7b";
  for (const y of [0.12, 1.35, 2.6]) {
    brush.fillRect(0, y * scale, width * scale, 0.15 * scale);
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

/** How wide the striped awning over a door in the square's wall is, in metres. */
const doorAwning = 2.4;

/**
 * How far a shop window's middle keeps from a door's: half its own awning (0.95 m) and half the door's (1.2 m), which
 * hang at the same height, so neither runs into the other (nor the door's frame, narrower than its awning).
 */
const windowClearOfDoor = 0.95 + doorAwning / 2;

/** The square's back wall: the ground floors of the houses behind it, shopfronts with striped awnings. */
export function paintTownSquareWall(brush: Brush, width: number, height: number, scale: number, doors: readonly DoorView[], map?: WorldMapView | null): void {
  const houses = townHouses(map);
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
      awning(brush, x(door.x), y(3.15), doorAwning * scale, scale, palette.mustard);
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
    brush.fillRect(x(m), 0, Math.min(0.25, width / 2 - m) * scale, height * scale);
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

  // What hangs on the wall, each placed from the wall's ends or its middle, and painted only where it fits: on the wall,
  // clear of the doorways and of what is already up.
  const taken: [number, number][] = [];
  const fits = (from: number, to: number) => {
    const ok = from >= -width / 2 + 0.1 && to <= width / 2 - 0.1
      && !doors.some((door) => door.x + 1 > from && door.x - 1 < to)
      && !taken.some(([a, b]) => b > from && a < to);
    if (ok) {
      taken.push([from, to]);
    }
    return ok;
  };
  // A window onto the street, near the left end.
  const window = -width / 2 + 0.4;
  if (fits(window, window + 1.4)) {
    frame(brush, x(window), y(3.3), 1.4 * scale, 1.8 * scale, scale, paints.windowSky);
    brush.fillStyle = palette.lightLeaf;
    brush.fillRect(x(window), y(1.95), 1.4 * scale, 0.45 * scale);
    brush.strokeStyle = ink;
    brush.lineWidth = Math.max(2, scale * 0.05);
    brush.beginPath();
    brush.moveTo(x(window + 0.7), y(3.3));
    brush.lineTo(x(window + 0.7), y(1.5));
    brush.moveTo(x(window), y(2.4));
    brush.lineTo(x(window + 1.4), y(2.4));
    brush.stroke();
  }
  // The menu, a little left of the middle: a chalkboard with a fish, and lines of chalk writing.
  const menu = -1.8;
  if (fits(menu, menu + 2.4)) {
    frame(brush, x(menu), y(3.4), 2.4 * scale, 1.75 * scale, scale, paints.chalkboard);
    brush.strokeStyle = paints.chalk;
    brush.fillStyle = paints.chalk;
    brush.lineWidth = Math.max(1.5, scale * 0.03);
    brush.font = `bold ${Math.round(0.3 * scale)}px sans-serif`;
    brush.textAlign = "center";
    brush.fillText("Menu", x(menu + 1.2), y(3.0));
    for (let line = 0; line < 3; line++) {
      brush.beginPath();
      brush.moveTo(x(menu + 0.3), y(2.6 - line * 0.32));
      brush.lineTo(x(menu + 1.45 - (line % 2) * 0.3), y(2.6 - line * 0.32));
      brush.stroke();
    }
    // A fish.
    brush.beginPath();
    brush.ellipse(x(menu + 1.85), y(2.35), 0.28 * scale, 0.14 * scale, 0, 0, Math.PI * 2);
    brush.moveTo(x(menu + 2.13), y(2.35));
    brush.lineTo(x(menu + 2.28), y(2.47));
    brush.lineTo(x(menu + 2.28), y(2.23));
    brush.closePath();
    brush.stroke();
  }
  // Shelves of cups and jars near the right end, over the counter.
  const shelves = width / 2 - 4.4;
  if (fits(shelves, shelves + 3.8)) {
    const cups = [palette.white, palette.teal, palette.mustard, palette.rose, palette.sage];
    for (const [shelf, count] of [[2.3, 6], [3.1, 5]] as const) {
      brush.fillStyle = palette.darkWood;
      brush.fillRect(x(shelves + 0.1), y(shelf), 3.6 * scale, 0.08 * scale);
      for (let i = 0; i < count; i++) {
        const left = shelves + 0.3 + i * (3.2 / count);
        brush.fillStyle = cups[(i + count) % cups.length] ?? palette.white;
        brush.fillRect(x(left), y(shelf + 0.32), 0.28 * scale, 0.32 * scale);
        brush.strokeStyle = ink;
        brush.lineWidth = Math.max(1.5, scale * 0.03);
        brush.strokeRect(x(left), y(shelf + 0.32), 0.28 * scale, 0.32 * scale);
      }
    }
  }
  // A framed picture of a fish, right of the middle.
  const picture = 1.2;
  if (fits(picture, picture + 1)) {
    frame(brush, x(picture), y(3.5), 1.0 * scale, 0.8 * scale, scale, paints.windowSky);
    brush.fillStyle = palette.mustard;
    brush.beginPath();
    brush.ellipse(x(picture + 0.45), y(3.1), 0.25 * scale, 0.13 * scale, 0, 0, Math.PI * 2);
    brush.fill();
  }
  // The line under the ceiling.
  brush.fillStyle = palette.darkWood;
  brush.fillRect(0, 0, width * scale, 0.12 * scale);
}

/** A home's floor: warm boards laid lengthways, a little paler than the café's. Rugs are the owner's to put down. */
export function paintHomeFloor(brush: Brush, plan: Floorplan, scale: number): void {
  const random = seeded(23);
  const [w, h] = [plan.width * scale, plan.depth * scale];
  const board = 0.36 * scale;
  const shades = [paints.homeBoard, paints.homeBoardLight, paints.homeBoardDark] as const;
  brush.lineWidth = Math.max(1.5, scale * 0.035);
  brush.strokeStyle = paints.homeSeam;
  for (let y = 0; y < h; y += board) {
    for (let x = -random() * 2.5 * scale; x < w; ) {
      const length = (1.8 + random() * 1.8) * scale;
      brush.fillStyle = pick(shades, random());
      brush.fillRect(x, y, length, board);
      brush.strokeRect(x, y, length, board);
      x += length;
    }
  }
}

/**
 * A home's back wall: sprigged wallpaper over a pale skirting board, a picture rail, and, each painted only where it fits
 * (on the wall, clear of the doorways), a window with its curtains drawn back, a framed fish and a shelf with a plant.
 */
export function paintHomeWall(brush: Brush, width: number, height: number, scale: number, doors: readonly DoorView[]): void {
  const x = (metres: number) => (metres + width / 2) * scale;
  const y = (metres: number) => (height - metres) * scale;
  brush.fillStyle = paints.wallpaper;
  brush.fillRect(0, 0, width * scale, height * scale);
  // The wallpaper's sprigs: little three-leaf sprays in rows, every other row shifted half along.
  brush.fillStyle = paints.wallpaperSprig;
  for (let row = 0, up = 0.9; up < height - 0.3; row++, up += 0.55) {
    for (let along = -width / 2 + 0.15 + (row % 2) * 0.3; along < width / 2 - 0.1; along += 0.6) {
      for (const [dx, dy] of [[0, 0.07], [-0.06, 0], [0.06, 0]] as const) {
        brush.beginPath();
        brush.ellipse(x(along + dx), y(up + dy), 0.035 * scale, 0.06 * scale, dx * 6, 0, Math.PI * 2);
        brush.fill();
      }
    }
  }
  // The skirting board, and the picture rail.
  brush.fillStyle = paints.skirting;
  brush.fillRect(0, y(0.35), width * scale, 0.35 * scale);
  brush.fillStyle = palette.wood;
  brush.fillRect(0, y(0.38), width * scale, 0.05 * scale);
  brush.fillRect(0, y(3.25), width * scale, 0.07 * scale);

  const taken: [number, number][] = [];
  const fits = (from: number, to: number) => {
    const ok = from >= -width / 2 + 0.1 && to <= width / 2 - 0.1
      && !doors.some((door) => door.x + 1 > from && door.x - 1 < to)
      && !taken.some(([a, b]) => b > from && a < to);
    if (ok) {
      taken.push([from, to]);
    }
    return ok;
  };
  // A window, a little right of the middle: sky, a sill, and pink curtains drawn back either side.
  const window = 0.6;
  if (fits(window - 0.5, window + 2.1)) {
    frame(brush, x(window), y(3.0), 1.6 * scale, 1.7 * scale, scale, paints.windowSky);
    brush.strokeStyle = ink;
    brush.lineWidth = Math.max(2, scale * 0.05);
    brush.beginPath();
    brush.moveTo(x(window + 0.8), y(3.0));
    brush.lineTo(x(window + 0.8), y(1.3));
    brush.moveTo(x(window), y(2.15));
    brush.lineTo(x(window + 1.6), y(2.15));
    brush.stroke();
    brush.fillStyle = palette.wood;
    brush.fillRect(x(window - 0.15), y(1.3), 1.9 * scale, 0.1 * scale);
    for (const side of [window - 0.45, window + 1.6]) {
      brush.beginPath();
      brush.moveTo(x(side), y(3.15));
      brush.lineTo(x(side + 0.45), y(3.15));
      brush.quadraticCurveTo(x(side + 0.15), y(2.2), x(side + 0.4), y(1.2));
      brush.lineTo(x(side), y(1.2));
      brush.closePath();
      brush.fillStyle = paints.curtain;
      brush.fill();
      brush.strokeStyle = ink;
      brush.lineWidth = Math.max(2, scale * 0.04);
      brush.stroke();
    }
  }
  // A framed picture of a fish, left of the window.
  const picture = -1.5;
  if (fits(picture, picture + 1)) {
    frame(brush, x(picture), y(2.9), 1.0 * scale, 0.8 * scale, scale, palette.cream);
    brush.fillStyle = palette.teal;
    brush.beginPath();
    brush.ellipse(x(picture + 0.45), y(2.5), 0.25 * scale, 0.13 * scale, 0, 0, Math.PI * 2);
    brush.moveTo(x(picture + 0.7), y(2.5));
    brush.lineTo(x(picture + 0.85), y(2.62));
    brush.lineTo(x(picture + 0.85), y(2.38));
    brush.closePath();
    brush.fill();
  }
  // A shelf near the right end, with a potted plant and two little jars on it.
  const shelf = width / 2 - 2.6;
  if (fits(shelf, shelf + 1.8)) {
    brush.fillStyle = palette.darkWood;
    brush.fillRect(x(shelf), y(2.3), 1.8 * scale, 0.08 * scale);
    brush.fillStyle = palette.pot;
    brush.fillRect(x(shelf + 0.2), y(2.62), 0.3 * scale, 0.32 * scale);
    brush.strokeStyle = ink;
    brush.lineWidth = Math.max(1.5, scale * 0.03);
    brush.strokeRect(x(shelf + 0.2), y(2.62), 0.3 * scale, 0.32 * scale);
    brush.fillStyle = palette.leaf;
    brush.beginPath();
    brush.arc(x(shelf + 0.35), y(2.8), 0.22 * scale, 0, Math.PI * 2);
    brush.fill();
    brush.stroke();
    for (const [left, colour] of [[shelf + 0.9, palette.sage], [shelf + 1.3, palette.rose]] as const) {
      brush.fillStyle = colour;
      brush.fillRect(x(left), y(2.6), 0.25 * scale, 0.3 * scale);
      brush.strokeRect(x(left), y(2.6), 0.25 * scale, 0.3 * scale);
    }
  }
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

/**
 * The power of two nearest <pixels> (up to the largest texture): the sides of a painted canvas. WebGL1 can't build
 * mipmaps for a texture whose sides aren't powers of two, and samples it black.
 */
export function powerOfTwo(pixels: number): number {
  return Math.min(largestTexture, 2 ** Math.max(0, Math.round(Math.log2(pixels))));
}

/** Where the shop's catalogue stand is (src/Pspsps.Domain/Rooms/RoomCatalog.cs): its rug is under it. */
const shopCatalogueStand = { x: 1.4, z: -0.6 };

/** The shop's floor: big tiles, mint and cream, in a check, and a round lilac rug in the middle under the catalogue. */
export function paintShopFloor(brush: Brush, plan: Floorplan, scale: number): void {
  const tile = 1 * scale;
  const [w, h] = [plan.width * scale, plan.depth * scale];
  for (let row = 0; row * tile < h; row++) {
    for (let column = 0; column * tile < w; column++) {
      brush.fillStyle = (row + column) % 2 === 0 ? paints.shopTile : paints.shopTileLight;
      brush.fillRect(column * tile, row * tile, tile, tile);
    }
  }
  brush.strokeStyle = paints.shopGrout;
  brush.lineWidth = Math.max(1, scale * 0.03);
  brush.beginPath();
  for (let x = 0; x <= w; x += tile) {
    brush.moveTo(x, 0);
    brush.lineTo(x, h);
  }
  for (let y = 0; y <= h; y += tile) {
    brush.moveTo(0, y);
    brush.lineTo(w, y);
  }
  brush.stroke();

  // The rug: lilac with a darker ring and a scalloped cream edge.
  const [cx, cy] = floorPixel(plan, scale, shopCatalogueStand.x, shopCatalogueStand.z);
  const [rx, ry] = [2.6 * scale, 1.9 * scale];
  brush.beginPath();
  brush.ellipse(cx, cy, rx, ry, 0, 0, Math.PI * 2);
  brush.fillStyle = palette.cream;
  brush.fill();
  brush.strokeStyle = ink;
  brush.lineWidth = Math.max(2, scale * 0.06);
  brush.stroke();
  brush.beginPath();
  brush.ellipse(cx, cy, rx - 0.25 * scale, ry - 0.25 * scale, 0, 0, Math.PI * 2);
  brush.fillStyle = paints.shopRug;
  brush.fill();
  brush.beginPath();
  brush.ellipse(cx, cy, rx - 0.7 * scale, ry - 0.6 * scale, 0, 0, Math.PI * 2);
  brush.strokeStyle = paints.shopRugDark;
  brush.lineWidth = Math.max(2, scale * 0.08);
  brush.stroke();
}

/**
 * The shop's back wall: striped butter wallpaper over sage panelling, bunting along the top, and, each painted only
 * where it fits (on the wall, clear of the doorways), a sign saying what the shop is, a poster of a top hat and a
 * window.
 */
export function paintShopWall(brush: Brush, width: number, height: number, scale: number, doors: readonly DoorView[]): void {
  const x = (metres: number) => (metres + width / 2) * scale;
  const y = (metres: number) => (height - metres) * scale;
  brush.fillStyle = paints.shopWall;
  brush.fillRect(0, 0, width * scale, height * scale);
  brush.fillStyle = paints.shopStripe;
  for (let m = -width / 2; m < width / 2; m += 0.5) {
    brush.fillRect(x(m), 0, Math.min(0.2, width / 2 - m) * scale, height * scale);
  }
  // Panelling, waist high, and its rail.
  brush.fillStyle = palette.sage;
  brush.fillRect(0, y(1.0), width * scale, 1.0 * scale);
  brush.strokeStyle = palette.teal;
  brush.lineWidth = Math.max(2, scale * 0.04);
  for (let m = -width / 2 + 0.15; m < width / 2 - 0.8; m += 0.9) {
    brush.strokeRect(x(m), y(0.85), 0.7 * scale, 0.7 * scale);
  }
  brush.fillStyle = palette.darkWood;
  brush.fillRect(0, y(1.06), width * scale, 0.08 * scale);

  // Bunting under the ceiling: little flags on a line, in turn pink, butter and sage.
  const flags = [palette.hatPink, palette.butter, palette.sage] as const;
  brush.strokeStyle = ink;
  brush.lineWidth = Math.max(1.5, scale * 0.03);
  for (let m = -width / 2 + 0.1, i = 0; m + 0.4 <= width / 2; m += 0.5, i++) {
    brush.beginPath();
    brush.moveTo(x(m), y(3.75));
    brush.lineTo(x(m + 0.4), y(3.75));
    brush.lineTo(x(m + 0.2), y(3.4));
    brush.closePath();
    brush.fillStyle = flags[i % flags.length] ?? palette.butter;
    brush.fill();
    brush.stroke();
  }

  const taken: [number, number][] = [];
  const fits = (from: number, to: number) => {
    const ok = from >= -width / 2 + 0.1 && to <= width / 2 - 0.1
      && !doors.some((door) => door.x + 1 > from && door.x - 1 < to)
      && !taken.some(([a, b]) => b > from && a < to);
    if (ok) {
      taken.push([from, to]);
    }
    return ok;
  };
  // The sign, over the shelves: a wooden board with the shop's name, and a party hat each side of it.
  const sign = -2.2;
  if (fits(sign, sign + 3.6)) {
    frame(brush, x(sign), y(3.25), 3.6 * scale, 0.85 * scale, scale, palette.darkWood);
    brush.fillStyle = palette.cream;
    brush.font = `bold ${Math.round(0.42 * scale)}px sans-serif`;
    brush.textAlign = "center";
    brush.fillText("Cat Shop", x(sign + 1.8), y(2.97));
    for (const at of [sign + 0.4, sign + 3.2]) {
      brush.beginPath();
      brush.moveTo(x(at - 0.16), y(2.55));
      brush.lineTo(x(at + 0.16), y(2.55));
      brush.lineTo(x(at), y(3.1));
      brush.closePath();
      brush.fillStyle = palette.hatPink;
      brush.fill();
      brush.stroke();
    }
  }
  // A poster of a top hat, right of the sign.
  const poster = 1.8;
  if (fits(poster, poster + 1.1)) {
    frame(brush, x(poster), y(3.1), 1.1 * scale, 1.2 * scale, scale, palette.cream);
    brush.fillStyle = palette.felt;
    brush.fillRect(x(poster + 0.35), y(2.75), 0.4 * scale, 0.55 * scale);
    brush.fillRect(x(poster + 0.2), y(2.2), 0.7 * scale, 0.08 * scale);
    brush.fillStyle = palette.rose;
    brush.fillRect(x(poster + 0.35), y(2.36), 0.4 * scale, 0.08 * scale);
  }
  // A window near the right end, over the counter.
  const window = width / 2 - 1.8;
  if (fits(window, window + 1.4)) {
    frame(brush, x(window), y(3.1), 1.4 * scale, 1.5 * scale, scale, paints.windowSky);
    brush.fillStyle = palette.lightLeaf;
    brush.fillRect(x(window), y(1.95), 1.4 * scale, 0.35 * scale);
    brush.strokeStyle = ink;
    brush.lineWidth = Math.max(2, scale * 0.05);
    brush.beginPath();
    brush.moveTo(x(window + 0.7), y(3.1));
    brush.lineTo(x(window + 0.7), y(1.6));
    brush.moveTo(x(window), y(2.35));
    brush.lineTo(x(window + 1.4), y(2.35));
    brush.stroke();
  }
  // The line under the ceiling.
  brush.fillStyle = palette.darkWood;
  brush.fillRect(0, 0, width * scale, 0.1 * scale);
}

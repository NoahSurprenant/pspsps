// Each room's look, read from the server (the server starts with RoomLooks.json): the sky (the colour round it), how its
// floor and back wall are painted, the ground round it, and the backdrop behind its back wall that the fixed camera sees
// over it. The browser only knows the painters, generators and wildlife by name (the lists the server checks a look
// against: src/Pspsps.Domain/Rooms/RoomLook.cs); which a room uses, and every colour and part, is data.
import {
  townHouses, paintCafeFloor, paintCafeWall, paintHomeFloor, paintHomeWall, paints, paintShopFloor, paintShopWall, paintStreet,
  paintGardenFloor, paintGardenWall, paintOrchardFloor, paintRiversideFloor, paintRiversideWall, paintTownSquareFloor, paintTownSquareWall,
  type Brush, type Floorplan,
} from "./paint";
import { paintBrickWall, paintChecker, paintPanelledWall, paintPlanks, paintTiles, type PaintColours } from "./paintPlain";
import { setOpenLandscapes } from "./doors";
import { paintSteps, painterRecipe, setRecipes, type PainterRecipe, type WildlifeRecipe } from "./recipes";
import { palette, type Part } from "./props";
import type { DoorView } from "./types";

export interface RoomLook {
  /** The colour behind everything: the sky, or an indoor room's dim. */
  sky: string;
  /** The floor's colour where its paint hasn't arrived (and a room without a painter's whole floor). */
  floorColour: string;
  /** The back wall's colour, on its top and ends (the front is painted). */
  wallColour: string;
  /** The ground round the floor, if any (a town's street): its colour, and a painting that repeats every <streetTile> metres. */
  ground?: { colour: string; paint?(brush: Brush, scale: number): void };
  paintFloor?(brush: Brush, plan: Floorplan, scale: number): void;
  /** Whether the floor's painter wants the room's props (the orchard paints round its trees). */
  floorNeedsProps: boolean;
  /** Paints the back wall's front, round <doorways>: the doors with a doorway in the wall (doors.ts, hasDoorway). */
  paintWall?(brush: Brush, width: number, height: number, scale: number, doorways: readonly DoorView[]): void;
  /** Open countryside: no back wall, and a path through the north edge as well. */
  openLandscape: boolean;
  /** A creature that lives in the room, drawn by wildlife.ts. */
  wildlife?: string;
  /** The seasonal variant wearing the look, when it is not the room's own: part of what a painted floor is kept under. */
  variant?: string;
  /** What stands behind the back wall (and round the room), in the room's own space. */
  backdrop(plan: Floorplan, wallHeight: number): Part[];
}

/** A room's look as the server keeps it. */
export interface RoomLookData {
  sky: string;
  floorColour: string;
  wallColour: string;
  ground?: { colour: string; painter?: string };
  floor?: { painter: string; colour?: string; accent?: string };
  wall?: { painter: string; colour?: string; accent?: string };
  openLandscape?: boolean;
  wildlife?: string;
  backdrop: { generator?: string; parts: Part[] };
}

/** A seasonal look as the server keeps it: colours, and optionally a floor of its own (a frozen pond or rink). */
export interface VariantData { id: string; sky: string; wallColour: string; floor?: { painter: string; colour?: string; accent?: string } | null }

export interface LooksData {
  rooms: { id: string; look: RoomLookData }[];
  variants: VariantData[];
  /** Painters and creatures the admins made as data; missing from an older server. */
  painters?: PainterRecipe[];
  wildlife?: WildlifeRecipe[];
}

/** Where Town Square's plaza is paved: round the spawn point (src/Pspsps.Domain/Rooms/StartingRooms.json), a little in front. */
const plaza = { x: 0, z: 3.4 };

type FloorPainter = (brush: Brush, plan: Floorplan, scale: number, colours: PaintColours) => void;
type WallPainter = (brush: Brush, width: number, height: number, scale: number, doors: readonly DoorView[], colours: PaintColours) => void;

/** The floor painters a look may name. */
export const floorPainters: Record<string, FloorPainter> = {
  "town-square": (brush, plan, scale) => paintTownSquareFloor(brush, plan, scale, plaza),
  cafe: paintCafeFloor,
  shop: paintShopFloor,
  garden: paintGardenFloor,
  "pear-orchard": paintOrchardFloor,
  riverside: paintRiversideFloor,
  home: paintHomeFloor,
  planks: paintPlanks,
  tiles: paintTiles,
  checker: paintChecker,
};

/** The back-wall painters a look may name. */
export const wallPainters: Record<string, WallPainter> = {
  "town-square": (brush, width, height, scale, doors) => paintTownSquareWall(brush, width, height, scale, doors),
  cafe: (brush, width, height, scale, doors) => paintCafeWall(brush, width, height, scale, doors),
  shop: (brush, width, height, scale, doors) => paintShopWall(brush, width, height, scale, doors),
  garden: (brush, width, height, scale) => paintGardenWall(brush, width, height, scale),
  riverside: (brush, width, height, scale) => paintRiversideWall(brush, width, height, scale),
  home: (brush, width, height, scale, doors) => paintHomeWall(brush, width, height, scale, doors),
  panelled: (brush, width, height, scale, _doors, colours) => paintPanelledWall(brush, width, height, scale, colours),
  bricks: (brush, width, height, scale, _doors, colours) => paintBrickWall(brush, width, height, scale, colours),
};

/** The ground painters a look may name. */
export const groundPainters: Record<string, (brush: Brush, scale: number) => void> = { street: paintStreet };

/** The backdrop generators a look may name: what is too much to write out as parts. */
export const backdropGenerators: Record<string, (plan: Floorplan, wallHeight: number) => Part[]> = { town: townBackdrop };

export const wildlifeKinds = ["fish", "butterfly"] as const;

/** A room no look is made for yet: a plain floor and wall under the sky. */
const plain: RoomLook = {
  sky: "#bfe3f2", floorColour: paints.grass, wallColour: "#f4efe4", floorNeedsProps: false, openLandscape: false, backdrop: () => [],
};

const rooms = new Map<string, RoomLook>();

/** What a seasonal overlay changes of a room's look, by name: its colours only, so any room can wear any of them. */
const variants = new Map<string, VariantData>();

type FloorPaint = { painter: string; colour?: string; accent?: string };

/** The painter a floor names: one the client has, or an admins' recipe; none for a name it doesn't know. */
function floorPainterOf(paint: FloorPaint | null | undefined): FloorPainter | undefined {
  if (!paint) {
    return undefined;
  }

  const recipe = painterRecipe("floor", paint.painter);
  return floorPainters[paint.painter]
    ?? (recipe ? (brush, plan, scale, colours) => paintSteps(brush, recipe.steps, plan.width, plan.depth, scale, colours) : undefined);
}

function lookFromData(data: RoomLookData): RoomLook {
  const wallRecipe = data.wall && painterRecipe("wall", data.wall.painter);
  const floor = floorPainterOf(data.floor);
  const wall = data.wall && (wallPainters[data.wall.painter]
    ?? (wallRecipe && ((brush, width, height, scale, _doors, colours) => paintSteps(brush, wallRecipe.steps, width, height, scale, colours))));
  const ground = data.ground?.painter ? groundPainters[data.ground.painter] : undefined;
  const generate = data.backdrop.generator ? backdropGenerators[data.backdrop.generator] : undefined;
  const floorColours = { colour: data.floor?.colour ?? data.floorColour, accent: data.floor?.accent };
  const wallColours = { colour: data.wall?.colour ?? data.wallColour, accent: data.wall?.accent };
  return {
    sky: data.sky,
    floorColour: data.floorColour,
    wallColour: data.wallColour,
    ground: data.ground ? { colour: data.ground.colour, paint: ground } : undefined,
    paintFloor: floor ? (brush, plan, scale) => floor(brush, plan, scale, floorColours) : undefined,
    floorNeedsProps: data.floor?.painter === "pear-orchard",
    paintWall: wall ? (brush, width, height, scale, doors) => wall(brush, width, height, scale, doors, wallColours) : undefined,
    openLandscape: data.openLandscape ?? false,
    wildlife: data.wildlife || undefined,
    backdrop: (plan, wallHeight) => [...(generate ? generate(plan, wallHeight) : []), ...data.backdrop.parts],
  };
}

export function setLooks(data: LooksData): void {
  rooms.clear();
  variants.clear();
  setRecipes(data.painters ?? [], data.wildlife ?? []);
  for (const { id, look } of data.rooms) {
    rooms.set(id, lookFromData(look));
  }
  for (const variant of data.variants) {
    variants.set(variant.id, variant);
  }
  setOpenLandscapes(data.rooms.filter(({ look }) => look.openLandscape).map(({ id }) => id));
}

/** Reads the looks from the server: every page load, so a new room's look needs no new client. */
export async function loadLooks(): Promise<void> {
  const response = await fetch("/api/looks");
  if (!response.ok) {
    throw new Error(`The room looks could not be read (${response.status}).`);
  }
  setLooks((await response.json()) as LooksData);
}

export function lookOf(roomId: string, variant?: string | null): RoomLook {
  const base = rooms.get(roomId) ?? plain;
  const changes = variant ? variants.get(variant) : undefined;
  if (!changes) {
    return base;
  }

  const look: RoomLook = { ...base, sky: changes.sky, wallColour: changes.wallColour, variant: variant ?? undefined };
  const floor = floorPainterOf(changes.floor);
  if (changes.floor && floor) {
    // The variant's floor replaces the room's: its own painter and colours (the room's floor colour where it names none).
    const colours = { colour: changes.floor.colour ?? base.floorColour, accent: changes.floor.accent };
    look.paintFloor = (brush, plan, scale) => floor(brush, plan, scale, colours);
    look.floorNeedsProps = changes.floor.painter === "pear-orchard";
  }

  return look;
}

/**
 * The rest of the houses whose ground floors are the square's back wall, standing behind it with their roofs, upper
 * windows and chimneys; trees behind them, hills and clouds further off. Past the square's ends the houses stand on the
 * street, so their ground floors have windows too.
 */
function townBackdrop(plan: Floorplan, wallHeight: number): Part[] {
  const parts: Part[] = [];
  const front = plan.depth / 2 + 0.3;
  for (const [i, house] of townHouses().entries()) {
    const depth = house.depth ?? 3.5;
    const width = house.to - house.from - 0.1;
    const middle = (house.from + house.to) / 2;
    parts.push({ shape: "box", width, height: house.height, depth, at: [middle, house.height / 2, front + depth / 2], colour: house.facade });
    // A gable roof: a long box turned 45 degrees, half sunk into the house.
    const side = (depth / 2 + 0.25) * Math.SQRT2;
    parts.push({
      shape: "box", width: width + 0.3, height: side, depth: side, at: [middle, house.height, front + depth / 2],
      turn: [Math.PI / 4, 0, 0], colour: house.roof,
    });
    if (i % 2 === 0) {
      parts.push({ shape: "box", width: 0.45, height: 1.6, depth: 0.45, at: [middle + width / 4, house.height + 1.2, front + depth / 2 + 0.4], colour: house.roof });
    }
    const windows = Math.max(1, Math.round(width / 1.9));
    for (let w = 0; w < windows; w++) {
      const x = house.from + 0.05 + (width / windows) * (w + 0.5);
      // The ground floor shows only past the square's ends: the back wall hides it, and its painted front stands for it.
      const besideTheSquare = Math.abs(x) - 0.4 >= plan.width / 2;
      const floors = [...(besideTheSquare ? [1.7] : []), wallHeight + 1.1, wallHeight + 2.7].filter((y) => y + 0.6 < house.height);
      for (const y of floors) {
        parts.push({ shape: "box", width: 0.8, height: 1.0, depth: 0.06, at: [x, y, front - 0.02], colour: paints.glass });
      }
    }
  }
  // Trees between the houses' roofs.
  for (const [x, height] of [[-21.5, 8.6], [-11, 7.8], [0, 8.4], [12, 7.8], [22, 8.4]] as const) {
    parts.push({ shape: "ball", diameter: 3.4, height: 3, at: [x, height, front + 11.5], colour: palette.leaf });
  }
  // Hills, far off.
  for (const [x, colour] of [[-30, palette.lightLeaf], [-8, palette.leaf], [16, palette.lightLeaf], [38, palette.leaf]] as const) {
    parts.push({ shape: "ball", diameter: 30, height: 16, at: [x, 0, front + 30], colour });
  }
  // Clouds.
  for (const [x, y] of [[-18, 17], [4, 20], [24, 16]] as const) {
    for (const [dx, dy, size] of [[0, 0, 4], [2.4, -0.4, 3], [-2.3, -0.6, 2.8]] as const) {
      parts.push({ shape: "ball", diameter: size, height: size * 0.7, at: [x + dx, y + dy, front + 26], colour: palette.white });
    }
  }
  return parts;
}

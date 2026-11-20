// Each room's look: the sky (the colour round it), how its floor and back wall are painted, the ground round it, and the
// backdrop behind its back wall that the fixed camera sees over it.
import {
  townHouses, paintCafeFloor, paintCafeWall, paintHomeFloor, paintHomeWall, paints, paintShopFloor, paintShopWall, paintStreet,
  paintGardenFloor, paintGardenWall, paintOrchardFloor, paintRiversideFloor, paintRiversideWall, paintTownSquareFloor, paintTownSquareWall,
  type Brush, type Floorplan,
} from "./paint";
import { ink, palette, type Part } from "./props";
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
  /** Paints the back wall's front, round <doorways>: the doors with a doorway in the wall (doors.ts, hasDoorway). */
  paintWall?(brush: Brush, width: number, height: number, scale: number, doorways: readonly DoorView[], map?: Floorplan["worldMap"]): void;
  /** What stands behind the back wall (and round the room), in the room's own space. */
  backdrop(plan: Floorplan, wallHeight: number): Part[];
}

/** Where Town Square's plaza is paved: round the spawn point (src/Pspsps.Domain/Rooms/RoomCatalog.cs), a little in front. */
const plaza = { x: 0, z: 3.4 };

const looks: Record<string, RoomLook> = {
  "town-square": {
    sky: "#bfe3f2",
    floorColour: paints.grass,
    wallColour: paints.plinth,
    ground: { colour: paints.street, paint: paintStreet },
    paintFloor: (brush, plan, scale) => paintTownSquareFloor(brush, plan, scale, plaza),
    paintWall: paintTownSquareWall,
    backdrop: townBackdrop,
  },
  cafe: {
    sky: "#e8d2b8",
    floorColour: paints.plank,
    wallColour: paints.cafeWall,
    paintFloor: paintCafeFloor,
    paintWall: paintCafeWall,
    backdrop: cafeBackdrop,
  },
  // The shop, off Town Square, where the catalogue is.
  shop: {
    sky: "#eadfc4",
    floorColour: paints.shopTile,
    wallColour: paints.shopWall,
    paintFloor: paintShopFloor,
    paintWall: paintShopWall,
    backdrop: shopBackdrop,
  },
  garden: {
    sky: "#d1eaf3",
    floorColour: paints.grass,
    wallColour: "#f0ddba",
    ground: { colour: "#b7cf98" },
    paintFloor: paintGardenFloor,
    paintWall: paintGardenWall,
    backdrop: gardenBackdrop,
  },
  "pear-orchard": {
    sky: "#e1edf2",
    floorColour: paints.grass,
    wallColour: "#91ad72",
    ground: { colour: "#a9c587" },
    paintFloor: paintOrchardFloor,
    backdrop: orchardBackdrop,
  },
  riverside: {
    sky: "#c1e2ef",
    floorColour: paints.grass,
    wallColour: "#ac805b",
    ground: { colour: "#acc98b" },
    paintFloor: paintRiversideFloor,
    paintWall: paintRiversideWall,
    backdrop: riversideBackdrop,
  },
  // Every player's home (src/Pspsps.Domain/Homes/Home.cs, Layout): one look, its furniture the owner's own.
  home: {
    sky: "#e9d8c8",
    floorColour: paints.homeBoard,
    wallColour: paints.wallpaper,
    paintFloor: paintHomeFloor,
    paintWall: paintHomeWall,
    backdrop: homeBackdrop,
  },
};

/** A room no look is made for yet: a plain floor and wall under the sky. */
const plain: RoomLook = { sky: "#bfe3f2", floorColour: paints.grass, wallColour: "#f4efe4", backdrop: () => [] };

export function lookOf(roomId: string): RoomLook {
  return looks[roomId] ?? plain;
}

function gardenBackdrop(plan: Floorplan): Part[] {
  // Clipped hedges and flowering canopies behind the courtyard's stone wall.
  const back = plan.depth / 2 + 3.2;
  return [
    { shape: "box", width: plan.width + 9, height: 4.15, depth: 1.6, at: [0, 2.08, back], colour: "#76a46b" },
    ...[-9, -4.5, 0, 4.5, 9].map((x, i): Part => ({
      shape: "ball", diameter: 5.6, height: i % 2 ? 3.8 : 4.8,
      at: [x, 4.8, back + 0.8], colour: i % 2 ? "#a9c681" : "#93b578",
    })),
    ...[-8.4, -5.4, -0.5, 4.9, 8.1].map((x, i): Part => ({
      shape: "ball", diameter: 0.7, at: [x, i % 2 ? 5.3 : 6, back - 1.15], colour: i % 2 ? "#f6d28e" : "#e8a5ad",
    })),
  ];
}

function orchardBackdrop(plan: Floorplan): Part[] {
  const trees: Part[] = [];
  for (const x of [-12, -8, -4, 0, 12]) {
    const z = plan.depth / 2 + 3;
    trees.push(
      { shape: "cylinder", diameter: 0.55, height: 3, at: [x, 1.5, z], colour: palette.wood },
      { shape: "ball", diameter: 3.8, height: 3.2, at: [x, 3.2, z], colour: "#84ab69" },
    );
    for (const dx of [-0.8, 0.6]) {
      trees.push({ shape: "ball", diameter: 0.38, height: 0.5, at: [x + dx, 3 + dx / 2, z - 1.6], colour: "#e8c960" });
    }
  }
  return trees;
}

function riversideBackdrop(plan: Floorplan): Part[] {
  const bank = plan.depth / 2 + 3;
  // A plank jetty and a red rowboat on its mooring, just off the trail.
  const jetty: Part[] = [];
  for (let i = 0; i < 12; i++) {
    jetty.push({ shape: "box", width: 1.6, height: 0.14, depth: 0.34, at: [-3, 0.35, bank - 1.4 + i * 0.35], colour: i % 2 ? "#c79e76" : "#c29570" });
  }
  for (const x of [-3.68, -2.32]) {
    for (const z of [bank - 1.25, bank + 2.35]) {
      jetty.push({ shape: "cylinder", diameter: 0.2, height: 1.1, at: [x, 0.55, z], colour: palette.darkWood });
    }
  }
  return [
    { shape: "box", width: 65, height: 0.25, depth: 9, at: [0, 0, bank + 4.5], colour: "#83bed4" },
    ...jetty,
    { shape: "box", width: 1.28, height: 0.25, depth: 2.6, at: [-1, 0.24, bank + 1.5], colour: "#88593e" },
    ...[-1, 1].flatMap((side): Part[] => [
      { shape: "box", width: 0.16, height: 0.43, depth: 2.65, at: [-1 + side * 0.63, 0.4, bank + 1.5], colour: "#b8604a" },
      { shape: "box", width: 0.16, height: 0.46, depth: 1.2, at: [-1 + side * 0.32, 0.43, bank + 3.28], turn: [0, -side * 0.55, 0], colour: "#b8604a" },
    ]),
    { shape: "box", width: 1.34, height: 0.43, depth: 0.16, at: [-1, 0.4, bank + 0.18], colour: "#b8604a" },
    { shape: "box", width: 1.55, depth: 0.32, height: 0.09, at: [-1, 0.69, bank + 1.5], colour: palette.wood },
    { shape: "box", width: 0.09, depth: 2.2, height: 0.09, at: [-1.4, 0.78, bank + 1.6], turn: [0, -0.38, 0], colour: palette.sisal },
    ...[-20, 0, 20].map((x, i): Part => ({
      shape: "ball", diameter: 28, height: 9 + i * 1.5,
      at: [x, 0.5, bank + 24], colour: i % 2 ? "#8bb987" : "#a0c78c",
    })),
    ...[-12, -9.8, 9.8, 12.2].map((x, i): Part => ({
      shape: "ball", diameter: 2.6, height: 5.1, at: [x, 3.2 + i % 2, bank + 0.2],
      colour: i % 2 ? "#689e75" : "#82b080",
    })),
  ];
}

/**
 * The rest of the houses whose ground floors are the square's back wall, standing behind it with their roofs, upper
 * windows and chimneys; trees behind them, hills and clouds further off. Past the square's ends the houses stand on the
 * street, so their ground floors have windows too.
 */
function townBackdrop(plan: Floorplan, wallHeight: number): Part[] {
  const parts: Part[] = [];
  const front = plan.depth / 2 + 0.3;
  for (const [i, house] of townHouses(plan.worldMap).entries()) {
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

/**
 * The café's walls: above the back wall's painted front, a cornice and more wall with a clock and two pictures on it; at
 * the sides, walls panelled like the back. Lamps hang over the counter.
 */
function cafeBackdrop(plan: Floorplan, wallHeight: number): Part[] {
  const [halfWidth, halfDepth] = [plan.width / 2, plan.depth / 2];
  const upper = 7;
  const face = halfDepth + 0.05;
  const parts: Part[] = [
    { shape: "box", width: plan.width + 0.7, height: upper, depth: 0.3, at: [0, wallHeight + upper / 2, halfDepth + 0.15], colour: paints.cafeWall },
    { shape: "box", width: plan.width + 0.9, height: 0.3, depth: 0.5, at: [0, wallHeight + 0.15, halfDepth + 0.05], colour: palette.darkWood },
    // A clock, at ten past ten.
    { shape: "cylinder", diameter: 1.5, height: 0.12, sides: 32, turn: [Math.PI / 2, 0, 0], at: [0, wallHeight + 2.3, face - 0.06], colour: palette.cream },
    { shape: "box", width: 0.06, height: 0.45, depth: 0.02, turn: [0, 0, -1], at: [0.19, wallHeight + 2.42, face - 0.14], colour: ink, outlined: false },
    { shape: "box", width: 0.06, height: 0.6, depth: 0.02, turn: [0, 0, 1], at: [-0.25, wallHeight + 2.46, face - 0.14], colour: ink, outlined: false },
  ];
  // Two pictures either side of the clock.
  for (const [x, colour] of [[-3.6, palette.sage], [3.6, palette.rose]] as const) {
    parts.push(
      { shape: "box", width: 1.4, height: 1.0, depth: 0.06, at: [x, wallHeight + 2.2, face - 0.03], colour: palette.darkWood },
      { shape: "box", width: 1.1, height: 0.7, depth: 0.04, at: [x, wallHeight + 2.2, face - 0.07], colour, outlined: false },
    );
  }
  for (const side of [-1, 1]) {
    const x = side * (halfWidth + 0.2);
    parts.push(
      { shape: "box", width: 0.3, height: wallHeight, depth: plan.depth + 0.3, at: [x, wallHeight / 2, 0.15], colour: paints.cafeWall },
      { shape: "box", width: 0.36, height: 1.1, depth: plan.depth + 0.36, at: [x, 0.55, 0.15], colour: palette.wood, outlined: false },
      { shape: "box", width: 0.4, height: 0.3, depth: plan.depth + 0.4, at: [x, wallHeight + 0.15, 0.15], colour: palette.darkWood },
    );
  }
  // Lamps over the counter, on cords up to the ceiling.
  const lamp = wallHeight - 1.45;
  const cord = wallHeight + upper - lamp;
  for (const x of [3.3, 5.5]) {
    parts.push(
      { shape: "box", width: 0.03, height: cord, depth: 0.03, at: [x, lamp + cord / 2, halfDepth - 1.3], colour: palette.iron, outlined: false },
      { shape: "cylinder", diameter: 0.6, top: 0.16, height: 0.36, sides: 16, at: [x, lamp, halfDepth - 1.3], colour: palette.mustard },
    );
  }
  return parts;
}

/**
 * A home's walls: above the back wall's painted front, a picture rail's worth more of wallpaper under a cornice; at the
 * sides, walls papered like the back, with their skirting. A paper lantern hangs from the ceiling, over the cats' heads.
 */
function homeBackdrop(plan: Floorplan, wallHeight: number): Part[] {
  const [halfWidth, halfDepth] = [plan.width / 2, plan.depth / 2];
  const upper = 7;
  const parts: Part[] = [
    { shape: "box", width: plan.width + 0.7, height: upper, depth: 0.3, at: [0, wallHeight + upper / 2, halfDepth + 0.15], colour: paints.wallpaper },
    { shape: "box", width: plan.width + 0.9, height: 0.25, depth: 0.45, at: [0, wallHeight + 0.125, halfDepth + 0.05], colour: paints.skirting },
  ];
  for (const side of [-1, 1]) {
    const x = side * (halfWidth + 0.2);
    parts.push(
      { shape: "box", width: 0.3, height: wallHeight, depth: plan.depth + 0.3, at: [x, wallHeight / 2, 0.15], colour: paints.wallpaper },
      { shape: "box", width: 0.36, height: 0.35, depth: plan.depth + 0.36, at: [x, 0.175, 0.15], colour: paints.skirting, outlined: false },
      { shape: "box", width: 0.4, height: 0.25, depth: plan.depth + 0.4, at: [x, wallHeight + 0.125, 0.15], colour: paints.skirting },
    );
  }
  // A round paper lantern on a cord, over the middle of the room and well above the cats.
  const lantern = wallHeight - 0.9;
  const cord = wallHeight + upper - lantern;
  parts.push(
    { shape: "box", width: 0.03, height: cord, depth: 0.03, at: [1.5, lantern + cord / 2, halfDepth - 2.5], colour: palette.iron, outlined: false },
    { shape: "ball", diameter: 0.7, at: [1.5, lantern, halfDepth - 2.5], colour: palette.glow },
  );
  return parts;
}

/**
 * The shop's walls: above the back wall's painted front, more wallpaper under a dark cornice; at the sides, walls like
 * the back, panelled. Two lamps hang over the catalogue, well above the cats.
 */
function shopBackdrop(plan: Floorplan, wallHeight: number): Part[] {
  const [halfWidth, halfDepth] = [plan.width / 2, plan.depth / 2];
  const upper = 7;
  const parts: Part[] = [
    { shape: "box", width: plan.width + 0.7, height: upper, depth: 0.3, at: [0, wallHeight + upper / 2, halfDepth + 0.15], colour: paints.shopWall },
    { shape: "box", width: plan.width + 0.9, height: 0.3, depth: 0.5, at: [0, wallHeight + 0.15, halfDepth + 0.05], colour: palette.darkWood },
  ];
  for (const side of [-1, 1]) {
    const x = side * (halfWidth + 0.2);
    parts.push(
      { shape: "box", width: 0.3, height: wallHeight, depth: plan.depth + 0.3, at: [x, wallHeight / 2, 0.15], colour: paints.shopWall },
      { shape: "box", width: 0.36, height: 1.0, depth: plan.depth + 0.36, at: [x, 0.5, 0.15], colour: palette.sage, outlined: false },
      { shape: "box", width: 0.4, height: 0.3, depth: plan.depth + 0.4, at: [x, wallHeight + 0.15, 0.15], colour: palette.darkWood },
    );
  }
  const lamp = wallHeight - 1.2;
  const cord = wallHeight + upper - lamp;
  for (const x of [0.2, 2.6]) {
    parts.push(
      { shape: "box", width: 0.03, height: cord, depth: 0.03, at: [x, lamp + cord / 2, halfDepth - 3], colour: palette.iron, outlined: false },
      { shape: "cylinder", diameter: 0.55, top: 0.2, height: 0.34, sides: 16, at: [x, lamp, halfDepth - 3], colour: palette.teal },
      { shape: "ball", diameter: 0.22, at: [x, lamp - 0.18, halfDepth - 3], colour: palette.glow },
    );
  }
  return parts;
}

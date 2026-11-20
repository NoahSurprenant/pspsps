// Floors and back walls any room can wear by naming one and giving it colours: they need no code of their own, so a new
// room in the editor is painted without a new client. (The rooms that started the game have painters of their own.)
import type { Brush, Floorplan } from "./paint";

/** The colours a room gives a painter: the main one, and the accent drawn over it (the main one, darkened, where left out). */
export interface PaintColours {
  colour: string;
  accent?: string;
}

/** A hex colour moved toward black (a negative <amount>) or white, to make an accent where a room gave none. */
export function shade(hex: string, amount: number): string {
  const channel = (at: number) => {
    const value = Number.parseInt(hex.slice(at, at + 2), 16);
    const moved = amount < 0 ? value * (1 + amount) : value + (255 - value) * amount;
    return Math.round(Math.min(255, Math.max(0, moved))).toString(16).padStart(2, "0");
  };
  return `#${channel(1)}${channel(3)}${channel(5)}`;
}

const accentOf = ({ colour, accent }: PaintColours) => accent ?? shade(colour, -0.18);

/** Boards running across the room, their joints staggered, with a dark line between. */
export function paintPlanks(brush: Brush, plan: Floorplan, scale: number, colours: PaintColours): void {
  const [w, h] = [plan.width * scale, plan.depth * scale];
  const board = 0.4 * scale;
  brush.fillStyle = colours.colour;
  brush.fillRect(0, 0, w, h);
  brush.strokeStyle = accentOf(colours);
  brush.lineWidth = Math.max(1, scale * 0.03);
  brush.beginPath();
  for (let row = 0, y = 0; y < h; row++, y += board) {
    brush.moveTo(0, y);
    brush.lineTo(w, y);
    for (let x = (row % 3) * 1.1 * scale; x < w; x += 3.3 * scale) {
      brush.moveTo(x, y);
      brush.lineTo(x, y + board);
    }
  }
  brush.stroke();
}

/** Square tiles in one colour with grout between. */
export function paintTiles(brush: Brush, plan: Floorplan, scale: number, colours: PaintColours): void {
  const [w, h] = [plan.width * scale, plan.depth * scale];
  const tile = 1 * scale;
  brush.fillStyle = colours.colour;
  brush.fillRect(0, 0, w, h);
  brush.strokeStyle = accentOf(colours);
  brush.lineWidth = Math.max(1, scale * 0.04);
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
}

/** Big tiles in a check of the colour and its accent. */
export function paintChecker(brush: Brush, plan: Floorplan, scale: number, colours: PaintColours): void {
  const [w, h] = [plan.width * scale, plan.depth * scale];
  const tile = 1 * scale;
  const accent = colours.accent ?? shade(colours.colour, 0.35);
  for (let row = 0; row * tile < h; row++) {
    for (let column = 0; column * tile < w; column++) {
      brush.fillStyle = (row + column) % 2 === 0 ? colours.colour : accent;
      brush.fillRect(column * tile, row * tile, tile, tile);
    }
  }
}

/** A wall with panelling waist high, under a rail. */
export function paintPanelledWall(brush: Brush, width: number, height: number, scale: number, colours: PaintColours): void {
  const [w, h] = [width * scale, height * scale];
  const accent = accentOf(colours);
  brush.fillStyle = colours.colour;
  brush.fillRect(0, 0, w, h);
  brush.fillStyle = accent;
  brush.fillRect(0, h - 1.1 * scale, w, 1.1 * scale);
  brush.strokeStyle = colours.colour;
  brush.lineWidth = Math.max(2, scale * 0.04);
  for (let m = 0.15 * scale; m + 0.7 * scale < w; m += 0.9 * scale) {
    brush.strokeRect(m, h - 0.95 * scale, 0.7 * scale, 0.8 * scale);
  }
  brush.fillStyle = shade(accent, -0.25);
  brush.fillRect(0, h - 1.16 * scale, w, 0.08 * scale);
}

/** A wall of bricks laid in rows, each row half a brick along from the last. */
export function paintBrickWall(brush: Brush, width: number, height: number, scale: number, colours: PaintColours): void {
  const [w, h] = [width * scale, height * scale];
  const [brickW, brickH] = [0.5 * scale, 0.2 * scale];
  brush.fillStyle = colours.colour;
  brush.fillRect(0, 0, w, h);
  brush.strokeStyle = accentOf(colours);
  brush.lineWidth = Math.max(1, scale * 0.025);
  brush.beginPath();
  for (let row = 0, y = 0; y < h; row++, y += brickH) {
    brush.moveTo(0, y);
    brush.lineTo(w, y);
    for (let x = row % 2 ? brickW / 2 : 0; x < w; x += brickW) {
      brush.moveTo(x, y);
      brush.lineTo(x, y + brickH);
    }
  }
  brush.stroke();
}

// Painters and creatures the admins make as data (the server's painter_recipes and wildlife_recipes tables; the rules they
// are checked against are src/Pspsps.Domain/Rooms/LookRecipes.cs), so a new one needs no new client.
import type { Brush } from "./paint";
import { accentOf, shade, type PaintColours } from "./paintPlain";
import type { Part } from "./props";

export interface PaintRepeat {
  dx: number;
  dy: number;
  shift?: number;
  cycle?: number;
}

export interface PaintStep {
  op: "fill" | "rect" | "checker";
  colour?: string;
  shade?: number;
  accent?: string;
  x?: number;
  y?: number;
  width?: number;
  height?: number;
  cell?: number;
  line?: number;
  outline?: boolean;
  repeat?: PaintRepeat;
}

export interface PainterRecipe {
  id: string;
  surface: "floor" | "wall";
  steps: PaintStep[];
}

export interface WildlifeMotion {
  kind: "orbit" | "shuttle" | "wander";
  speed: number;
  height: number;
  reach?: number;
  prop?: string;
  bob?: number;
  sway?: number;
}

export interface WildlifeRecipe {
  id: string;
  parts: Part[];
  motion: WildlifeMotion;
}

/** The most rectangles one painting draws, whatever the recipe asks for. */
export const mostRects = 4000;

const recipes = {
  painters: new Map<string, PainterRecipe>(),
  wildlife: new Map<string, WildlifeRecipe>(),
};

export function setRecipes(painters: PainterRecipe[], wildlife: WildlifeRecipe[]): void {
  recipes.painters.clear();
  recipes.wildlife.clear();
  for (const painter of painters) {
    recipes.painters.set(`${painter.surface}:${painter.id}`, painter);
  }
  for (const creature of wildlife) {
    recipes.wildlife.set(creature.id, creature);
  }
}

export const painterRecipe = (surface: "floor" | "wall", name: string) => recipes.painters.get(`${surface}:${name}`);

export const wildlifeRecipe = (name: string) => recipes.wildlife.get(name);

function colourOf(reference: string | undefined, amount: number | undefined, colours: PaintColours): string {
  const base = reference === "accent" ? accentOf(colours) : reference?.startsWith("#") ? reference : colours.colour;
  return amount ? shade(base, amount) : base;
}

/** Draws a recipe's steps over a surface of <width> by <height> metres. */
export function paintSteps(brush: Brush, steps: readonly PaintStep[], width: number, height: number, scale: number, colours: PaintColours): void {
  let drawn = 0;
  const rect = (step: PaintStep, x: number, y: number) => {
    if (drawn++ >= mostRects) {
      return;
    }
    const [w, h] = [(step.width ?? 0) * scale, (step.height ?? 0) * scale];
    if (step.outline) {
      brush.lineWidth = Math.max(1, (step.line ?? 0.03) * scale);
      brush.strokeRect(x * scale, y * scale, w, h);
    } else {
      brush.fillRect(x * scale, y * scale, w, h);
    }
  };
  for (const step of steps) {
    const colour = colourOf(step.colour, step.shade, colours);
    brush.fillStyle = colour;
    brush.strokeStyle = colour;
    if (step.op === "fill") {
      brush.fillRect(0, 0, width * scale, height * scale);
    } else if (step.op === "checker") {
      const cell = Math.max(0.1, step.cell ?? 1);
      const other = colourOf(step.accent ?? "accent", step.shade, colours);
      for (let row = 0, y = 0; y < height && drawn < mostRects; row++, y += cell) {
        for (let column = 0, x = 0; x < width && drawn < mostRects; column++, x += cell) {
          brush.fillStyle = (row + column) % 2 === 0 ? colour : other;
          drawn++;
          brush.fillRect(x * scale, y * scale, cell * scale, cell * scale);
        }
      }
    } else if (step.op === "rect") {
      const { dx = 0, dy = 0, shift = 0, cycle = 2 } = step.repeat ?? {};
      const [x0, y0] = [step.x ?? 0, step.y ?? 0];
      const rowStep = step.repeat && dy >= 0.1 ? dy : height + 1;
      for (let row = 0, y = y0; y < height && drawn < mostRects; row++, y += rowStep) {
        const along = x0 + (row % Math.max(1, cycle)) * shift;
        const columnStep = step.repeat && dx >= 0.1 ? dx : width + 1;
        for (let x = along; x < width && drawn < mostRects; x += columnStep) {
          rect(step, x, y);
        }
      }
    }
  }
}

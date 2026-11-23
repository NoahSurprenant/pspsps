import type { VariantData } from "./looks";

/** The body a recipe is saved with (its name is in the address), from the entry the list shows. */
export function recipeBody(kind: "painters" | "wildlife", entry: Record<string, unknown>): Record<string, unknown> {
  return kind === "painters" ? { surface: entry.surface, steps: entry.steps } : { parts: entry.parts, motion: entry.motion };
}

export function newPainterTemplate(): unknown {
  return { id: "stripes", surface: "floor", steps: [{ op: "fill", colour: "main" }, { op: "rect", colour: "accent", x: 0, y: 0, width: 0.5, height: 100, repeat: { dx: 1, dy: 0 } }] };
}

export function newWildlifeTemplate(): unknown {
  return { id: "moth", parts: [{ shape: "ball", diameter: 0.2, height: 0.05, at: [0, 0, 0], colour: "#d9d2c3" }], motion: { kind: "wander", speed: 0.6, height: 1.2, reach: 3 } };
}

/** A starting variant: a wintry sky and wall, no floor of its own. */
export function blankVariant(id: string): VariantData {
  return { id, sky: "#dfe9f2", wallColour: "#e6dccb" };
}

/** The body a variant is saved with (its name is in the address): colours, and a floor of its own only when it has one. */
export function variantBody(variant: VariantData): Omit<VariantData, "id"> {
  return { sky: variant.sky, wallColour: variant.wallColour, ...(variant.floor ? { floor: variant.floor } : {}) };
}

/** The variant with its own floor set (a painter, and colours) or taken away (no painter). */
export function withVariantFloor(variant: VariantData, change: { painter?: string; colour?: string; accent?: string }): VariantData {
  const next = { ...variant.floor, ...change };
  const rest = { id: variant.id, sky: variant.sky, wallColour: variant.wallColour };
  return next.painter ? { ...rest, floor: { painter: next.painter, ...(next.colour ? { colour: next.colour } : {}), ...(next.accent ? { accent: next.accent } : {}) } } : rest;
}

/** One recipe's text as the entry to save, with its id; the problem in words when it isn't one. */
export function recipeFrom(text: string, kind: "painters" | "wildlife"): { entry: Record<string, unknown> } | { problem: string } {
  let parsed: unknown;
  try {
    parsed = JSON.parse(text);
  } catch {
    return { problem: "That isn't valid JSON." };
  }
  if (typeof parsed !== "object" || parsed === null || Array.isArray(parsed)) return { problem: "A recipe is one JSON object." };
  const entry = parsed as Record<string, unknown>;
  if (typeof entry.id !== "string" || entry.id.trim() === "") return { problem: "A recipe needs an id." };
  const needed = kind === "painters" ? ["surface", "steps"] : ["parts"];
  const missing = needed.filter((key) => entry[key] === undefined);
  return missing.length > 0 ? { problem: `A ${kind === "painters" ? "painter" : "creature"} needs ${missing.join(" and ")}.` } : { entry };
}

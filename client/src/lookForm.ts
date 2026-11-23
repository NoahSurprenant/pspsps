import type { RoomLookData } from "./looks";

/** The three things a look paints with a named painter and colours. */
export type PainterPart = "floor" | "wall" | "ground";

interface Paint { painter?: string; colour?: string; accent?: string }

const without = (look: RoomLookData, key: keyof RoomLookData): RoomLookData =>
  Object.fromEntries(Object.entries(look).filter(([name]) => name !== key)) as unknown as RoomLookData;

/** A starting look for a room that has none: plain colours, a plain floor and wall, nothing behind. */
export function blankLook(): RoomLookData {
  return { sky: "#cfe8ff", floorColour: "#c8b79a", wallColour: "#e6dccb", floor: { painter: "planks", colour: "#b08a5a" }, wall: { painter: "panelled" }, backdrop: { parts: [] } };
}

/** The look with one of its top-level fields changed; empty text clears an optional one (wildlife). */
export function withField(look: RoomLookData, field: "sky" | "floorColour" | "wallColour" | "wildlife", value: string): RoomLookData {
  if (field === "wildlife") {
    const rest = without(look, "wildlife");
    return value === "" ? rest : { ...rest, wildlife: value };
  }
  return { ...look, [field]: value };
}

/** The look with a part's painter, colour or accent changed; no painter takes a floor or wall away. */
export function withPainter(look: RoomLookData, part: PainterPart, change: Paint): RoomLookData {
  const rest = without(look, part);
  const next: Paint = { ...(look[part] as Paint | undefined), ...change };
  if (part === "ground") {
    // A ground has a colour always, and a painter only when chosen.
    return { ...rest, ground: { colour: next.colour ?? "#8a8a8a", ...(next.painter ? { painter: next.painter } : {}) } };
  }
  if (!next.painter) return rest;
  return { ...rest, [part]: { painter: next.painter, ...(next.colour ? { colour: next.colour } : {}), ...(next.accent ? { accent: next.accent } : {}) } };
}

/** The look without its ground (a room with nothing round its floor). */
export const withoutGround = (look: RoomLookData): RoomLookData => without(look, "ground");

/** The look with its open-landscape flag set (no back wall, a path through the north edge). */
export function withOpenLandscape(look: RoomLookData, open: boolean): RoomLookData {
  const rest = without(look, "openLandscape");
  return open ? { ...rest, openLandscape: true } : rest;
}

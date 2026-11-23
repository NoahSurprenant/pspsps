import type { RoomLookData } from "./looks";

/** The three things a look paints with a named painter and colours. */
export type PainterPart = "floor" | "wall" | "ground";

/** A starting look for a room that has none: plain colours, a plain floor and wall, nothing behind. */
export function blankLook(): RoomLookData {
  return { sky: "#cfe8ff", floorColour: "#c8b79a", wallColour: "#e6dccb", floor: { painter: "planks", colour: "#b08a5a" }, wall: { painter: "panelled" }, backdrop: { parts: [] } };
}

/** The look with one of its top-level fields changed; empty text clears an optional one (wildlife). */
export function withField(look: RoomLookData, field: "sky" | "floorColour" | "wallColour" | "wildlife", value: string): RoomLookData {
  if (field === "wildlife") {
    const { wildlife: _gone, ...rest } = look;
    return value === "" ? rest : { ...rest, wildlife: value };
  }
  return { ...look, [field]: value };
}

/** The look with a part's painter, colour or accent changed; no painter takes the part away. Colours are kept only while it has a painter. */
export function withPainter(look: RoomLookData, part: PainterPart, change: { painter?: string; colour?: string; accent?: string }): RoomLookData {
  const { [part]: current, ...rest } = look as RoomLookData & Record<PainterPart, { painter?: string; colour?: string; accent?: string } | undefined>;
  const next = { ...current, ...change };
  if (part === "ground") {
    // A ground has a colour always, and a painter only when chosen.
    const { accent: _unused, ...ground } = next;
    return { ...rest, ground: { colour: ground.colour ?? "#8a8a8a", ...(ground.painter ? { painter: ground.painter } : {}) } };
  }
  if (!next.painter) return rest as RoomLookData;
  const { painter, colour, accent } = next;
  return { ...rest, [part]: { painter, ...(colour ? { colour } : {}), ...(accent ? { accent } : {}) } } as RoomLookData;
}

/** The look without its ground (a room with nothing round its floor). */
export function withoutGround(look: RoomLookData): RoomLookData {
  const { ground: _gone, ...rest } = look;
  return rest;
}

/** The look with its open-landscape flag set (no back wall, a path through the north edge). */
export function withOpenLandscape(look: RoomLookData, open: boolean): RoomLookData {
  const { openLandscape: _gone, ...rest } = look;
  return open ? { ...rest, openLandscape: true } : rest;
}

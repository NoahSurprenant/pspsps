import { windowOf, type WindowFields, type WindowJson } from "./overlayWindow";

/** A seasonal overlay as the server keeps it: props added and taken away, and a look to paint the room in meanwhile. */
export interface OverlayData {
  id: string;
  roomId?: string;
  name: string;
  window: WindowJson;
  adds: unknown[];
  removes: unknown[];
  look?: string | null;
}

/** A new overlay for a room, to edit: a month in the coming winter, repeating every year, changing nothing yet. */
export function blankOverlay(id: string): OverlayData {
  return { id, name: id, window: windowOf({ start: "2000-12-01", end: "2000-12-31", yearly: true }), adds: [], removes: [] };
}

/** The overlay with its window set from the picker's fields. */
export function withWindow(overlay: OverlayData, fields: WindowFields): OverlayData {
  return { ...overlay, window: windowOf(fields) };
}

/** The overlay with a look chosen (empty text takes the look off: the room's own). */
export function withLook(overlay: OverlayData, look: string): OverlayData {
  return { ...overlay, look: look === "" ? null : look };
}

/** A list of props written as JSON, or what is wrong with the text. */
export function propsFrom(text: string, what: string): { props: unknown[] } | { problem: string } {
  try {
    const parsed: unknown = JSON.parse(text);
    return Array.isArray(parsed) ? { props: parsed } : { problem: `${what} is a JSON list of props.` };
  } catch {
    return { problem: `${what} isn't valid JSON.` };
  }
}

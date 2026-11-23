import type { RoomLookData } from "./looks";
import type { RoomSnapshot } from "./types";

/** What the room editor tells the 3D preview page (a frame on the same page) to draw. */
export interface PreviewMessage {
  type: "room-preview";
  roomId: string;
  snapshot: RoomSnapshot;
  draftLook?: RoomLookData;
}

/** What the preview page tells the editor once it can draw (anything sent before is lost). */
export const PREVIEW_READY = { type: "room-preview-ready" } as const;

/** Where the server makes a snapshot of an unsaved layout. */
export function unsavedPreviewUrl(roomId: string, overlayId?: string): string {
  const query = overlayId ? `?overlay=${encodeURIComponent(overlayId)}` : "";
  return `/api/admin/rooms/${encodeURIComponent(roomId)}/preview${query}`;
}

/** The message in a page message's data, or null when it is not one (the browser, and extensions, send others). */
export function asPreviewMessage(data: unknown): PreviewMessage | null {
  if (typeof data !== "object" || data === null) {
    return null;
  }
  const { type, roomId, snapshot } = data as Partial<PreviewMessage>;
  return type === "room-preview" && typeof roomId === "string" && typeof snapshot === "object" && snapshot !== null
    ? (data as PreviewMessage)
    : null;
}

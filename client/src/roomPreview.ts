import type { RoomLookData } from "./looks";
import type { RoomSnapshot } from "./types";

/** Where the server gives a room as it would be with its drafts in place; with an overlay, that overlay on. */
export function previewUrl(roomId: string, overlayId?: string): string {
  const query = overlayId ? `?overlay=${encodeURIComponent(overlayId)}` : "";
  return `/api/admin/rooms/${encodeURIComponent(roomId)}/preview${query}`;
}

export interface RoomPreview {
  /** Draws a room already in hand (a snapshot, with the look its draft brings). */
  draw(roomId: string, snapshot: RoomSnapshot, draftLook?: RoomLookData): Promise<void>;
  /** Draws the room as the server says it would be (the draft and the overlay in place); throws the server's message when it can't be. */
  show(roomId: string, overlayId?: string): Promise<void>;
}

/**
 * The real 3D scene on an admin-page canvas, with no cats and no connection. The scene and Babylon load on the first
 * preview, not with the admin page.
 */
export function roomPreview(canvas: HTMLCanvasElement): RoomPreview {
  let scene: Promise<{ show: (snapshot: RoomSnapshot) => void }> | undefined;
  const make = async (): Promise<{ show: (snapshot: RoomSnapshot) => void }> => {
    const [{ Engine }, { RoomScene }, { loadPropDesigns }, { loadLooks }] = await Promise.all([
      import("@babylonjs/core/Engines/engine"), import("./scene"), import("./props"), import("./looks"),
    ]);
    await Promise.all([loadPropDesigns(), loadLooks()]);
    const engine = new Engine(canvas, true, { stencil: true, canvasTabIndex: -1 });
    const made = new RoomScene(engine, undefined, false);
    made.markArrival = true;
    window.addEventListener("resize", () => engine.resize());
    return made;
  };
  return {
    async show(roomId, overlayId) {
      const response = await fetch(previewUrl(roomId, overlayId));
      if (!response.ok) {
        const said = (await response.json().catch(() => null)) as { message?: string } | null;
        throw new Error(said?.message ?? `The preview could not be made (${response.status}).`);
      }
      const { draftLook, ...snapshot } = (await response.json()) as RoomSnapshot & { draftLook?: RoomLookData };
      await this.draw(roomId, snapshot, draftLook);
    },
    async draw(roomId, snapshot, draftLook) {
      // The scene first (making it reads the live looks, which would wipe a draft look set before it), then the live looks as
      // they are now with the draft's over its room's: a draft that has been discarded or published leaves nothing behind.
      scene ??= make();
      const made = await scene;
      const { loadLooks, setDraftLook } = await import("./looks");
      await loadLooks();
      if (draftLook) {
        setDraftLook(roomId, draftLook);
      }
      made.show(snapshot);
    },
  };
}

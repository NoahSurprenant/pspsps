import { Injectable } from "@angular/core";
import { draftQuery, publishQuery, type LookOptions, type RoomDetail, type RoomSummary } from "../../src/roomsData";
import { unsavedPreviewUrl } from "../../src/previewMessage";
import { type CanvasDoor, isLayout } from "../../src/roomCanvas";
import type { RoomLookData, VariantData } from "../../src/looks";
import type { RoomSnapshot } from "../../src/types";
import { request } from "./http";

export interface LooksLibrary {
  variants: VariantData[];
  painters?: Record<string, unknown>[];
  wildlife?: Record<string, unknown>[];
}

export type RoomSnapshotWithLook = RoomSnapshot & { draftLook?: RoomLookData };

const base = (roomId: string): string => `/api/admin/rooms/${encodeURIComponent(roomId)}`;

/** What the room editor asks the server to read and do; the server checks every layout. */
@Injectable({ providedIn: "root" })
export class RoomsApi {
  list = (): Promise<RoomSummary[]> => request("GET", "/api/admin/rooms");
  detail = (roomId: string): Promise<RoomDetail> => request("GET", base(roomId));
  saveDraft = (roomId: string, layout: unknown, revision: number | null | undefined): Promise<{ problems: string[] } | null> =>
    request("PUT", `${base(roomId)}/draft${draftQuery(revision)}`, layout);
  discardDraft = (roomId: string): Promise<null> => request("DELETE", `${base(roomId)}/draft`);
  publish = (roomId: string, together: string): Promise<null> => request("POST", `${base(roomId)}/publish${publishQuery(together)}`);
  rollback = (roomId: string, version: number): Promise<null> => request("POST", `${base(roomId)}/rollback/${version}`);
  options = (): Promise<LookOptions> => request("GET", "/api/admin/looks/options");
  looks = (): Promise<LooksLibrary> => request("GET", "/api/looks");
  saveVariant = (id: string, variant: unknown): Promise<null> => request("PUT", `/api/admin/looks/variants/${encodeURIComponent(id)}`, variant);
  deleteVariant = (id: string): Promise<null> => request("DELETE", `/api/admin/looks/variants/${encodeURIComponent(id)}`);
  saveRecipe = (kind: "painters" | "wildlife", id: string, body: unknown): Promise<null> => request("PUT", `/api/admin/looks/${kind}/${encodeURIComponent(id)}`, body);
  deleteRecipe = (kind: "painters" | "wildlife", id: string): Promise<null> => request("DELETE", `/api/admin/looks/${kind}/${encodeURIComponent(id)}`);

  /** The look the room has live (null when it has none yet). */
  liveLook = async (roomId: string): Promise<RoomLookData | null> =>
    (await request<{ rooms: { id: string; look: RoomLookData }[] }>("GET", "/api/looks")).rooms.find((entry) => entry.id === roomId)?.look ?? null;
  saveLook = (roomId: string, look: RoomLookData): Promise<null> => request("PUT", `/api/admin/looks/rooms/${encodeURIComponent(roomId)}`, look);
  clearDraftLook = (roomId: string): Promise<null> => request("DELETE", `/api/admin/looks/rooms/${encodeURIComponent(roomId)}/draft`);
  /** The room as it would be with this layout (and look) in it, as the browser is sent a room: what the preview draws. */
  preview = (roomId: string, layout: unknown, look: unknown): Promise<RoomSnapshotWithLook> =>
    request("POST", unsavedPreviewUrl(roomId), { layout, look: look ?? null });

  /** Adds the way back to another room's draft, which a new door here needs. */
  addWayBack = async (door: CanvasDoor, back: CanvasDoor): Promise<void> => {
    const other = await this.detail(door.toRoom);
    const otherLayout = other.draft ?? other.published;
    if (!isLayout(otherLayout)) throw new Error(`${door.toRoom} has no layout to add the way back to.`);
    await this.saveDraft(door.toRoom, { ...otherLayout, doors: [...otherLayout.doors, back] }, other.draftRevision);
  };
}

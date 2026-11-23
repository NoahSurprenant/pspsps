import { Injectable } from "@angular/core";
import { draftQuery, publishQuery, type RoomDetail, type RoomSummary } from "../../src/roomsData";
import { unsavedPreviewUrl } from "../../src/previewMessage";
import { type CanvasDoor, isLayout } from "../../src/roomCanvas";
import type { RoomLookData } from "../../src/looks";
import type { RoomSnapshot } from "../../src/types";
import { request } from "./http";

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
  propKinds = async (): Promise<string[]> => (await request<{ propKinds: string[] }>("GET", "/api/admin/looks/options")).propKinds;
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

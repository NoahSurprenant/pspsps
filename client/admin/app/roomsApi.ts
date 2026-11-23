import { Injectable } from "@angular/core";
import { draftQuery, publishQuery, type RoomDetail, type RoomSummary } from "../../src/roomsData";
import { request } from "./http";

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
}

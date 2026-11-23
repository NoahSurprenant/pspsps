import type { OverlayData } from "./overlayData";
import type { RoomLookData } from "./looks";
// What the admin reads about rooms, and the small pure rules for talking to the server about them. No DOM and no 3D, so the
// room editor app can use it as well as the admin page.

/** The query that publishes a room together with others: new rooms need the rooms whose doors lead to them. */
export function publishQuery(together: string, goLiveAt?: string): string {
  const ids = together.split(/[\s,]+/).filter((id) => id.length > 0);
  const parts = [...ids.map((id) => `with=${encodeURIComponent(id)}`), ...(goLiveAt ? [`at=${encodeURIComponent(goLiveAt)}`] : [])];
  return parts.length === 0 ? "" : `?${parts.join("&")}`;
}

/** The time a date-time field holds (the admin's local time) as the instant the server is told; null when it is empty or not a time. */
export function goLiveInstant(local: string): string | null {
  const time = local === "" ? Number.NaN : new Date(local).getTime();
  return Number.isNaN(time) ? null : new Date(time).toISOString();
}

/** Some of the drafts waiting, published together: the first is the room the request is for, the rest ride along. */
export function draftSetUrl(ids: string[], goLiveAt?: string): { roomId: string; query: string } | null {
  const [roomId, ...rest] = ids;
  return roomId === undefined ? null : { roomId, query: publishQuery(rest.join(","), goLiveAt) };
}

/** The query that saves over the draft as it was opened, so a draft someone else saved meanwhile is refused; none for a new draft. */
export function draftQuery(revision: number | null | undefined): string {
  return revision === null || revision === undefined ? "" : `?revision=${revision}`;
}

export interface AuditLine { at: string; adminId: string | null; action: string; target: string; detail: string }
export interface HistoryEntry { id: string; at: string; adminId: string | null; action: string; detail: string; layout: unknown; look: RoomLookData | null }

/** One line of what admins did: when, what, to which room and the detail. */
export function auditText(line: AuditLine): string {
  const detail = line.detail === "" ? "" : ` (${line.detail})`;
  return `${line.at.slice(0, 16).replace("T", " ")} ${line.action} ${line.target}${detail}`;
}

export interface RoomSummary { roomId: string; name: string; published: number | null; draft: number | null; goLiveAt?: string | null }
export interface VersionSummary { number: number; state: string; createdAt: string; publishedAt: string | null }
export interface RoomDetail {
  roomId: string;
  versions: VersionSummary[];
  published: unknown | null;
  draft: unknown | null;
  overlays: OverlayData[];
  /** Overlays of this room in the reviewed content file that are not saved yet (they wait for the room to be published); previewable. */
  draftRevision?: number | null;
  goLiveAt?: string | null;
  /** The look the draft brings, which goes live when the room is published; missing when the draft leaves the room's look as it is. */
  draftLook?: unknown | null;
}

/** When a draft goes live by itself, as the admin reads it; empty for a draft that waits to be published by hand. */
export function goLiveText(goLiveAt: string | null | undefined): string {
  return goLiveAt ? `goes live ${new Date(goLiveAt).toLocaleString()}` : "";
}

/** One line for a room in the list: its name, the version players have and whether a draft waits. */
export function roomLine(room: RoomSummary): string {
  const live = room.published === null ? "not published" : `version ${room.published} live`;
  if (room.draft === null) return `${room.name} (${live})`;
  const when = goLiveText(room.goLiveAt);
  const goLive = when === "" ? "" : `, ${when}`;
  return `${room.name} (${live}, draft ${room.draft}${goLive})`;
}

/** The layout or overlay as text for editing; null when it isn't valid JSON. */
export function parseJson(text: string): unknown | null {
  try {
    return JSON.parse(text) as unknown;
  } catch {
    return null;
  }
}

/** The text of what the server said was wrong with a draft, or that it's ready. */
export function problemsText(problems: string[]): string {
  return problems.length === 0 ? "Saved. It can be published." : `Saved, but it can't be published yet: ${problems.join(" ")}`;
}


/** The names a look may use, which the server checks it against. */
export interface LookOptions {
  propKinds: string[];
  floors: string[];
  walls: string[];
  grounds: string[];
  generators: string[];
  wildlife: string[];
}

export interface ImportReport { applied: string[]; unchanged: string[]; problems: string[] }

/** What loading the repository's content file did, in words for the admin. */
export function importText(report: ImportReport | null): string {
  if (report === null) return "Nothing came back.";
  const lines = [...report.applied, ...report.problems.map((problem) => `Not applied: ${problem}`)];
  return lines.length > 0 ? lines.join("\n") : `Nothing to load: ${report.unchanged.length} items already match.`;
}

/** Where the published content is downloaded as JSON, to commit to the repository for review. */
export const exportUrl = "/api/admin/export";

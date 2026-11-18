// The admin page's contents (/admin, admin.html): who is banned or muted now, and what admins did lately
// (src/Pspsps.Server/Moderation/AdminEndpoints.cs, /api/admin/overview).

import { untilText, type TimeFormat } from "./moderation";

/** A ban or mute in force, on the admin page (Contracts.cs, AdminSanctionView). */
export interface AdminSanction {
  reason: string;
  since: string;
  until: string | null;
  byId: string;
  byName: string;
}

/** A player banned or muted now (Contracts.cs, SanctionedPlayerView). */
export interface SanctionedPlayer {
  playerId: string;
  displayName: string;
  ban: AdminSanction | null;
  mute: AdminSanction | null;
}

/** What an admin did (Contracts.cs, ModerationLogView). */
export type ModerationKind = "Warned" | "Muted" | "Banned" | "MuteLifted" | "BanLifted";

export interface ModerationLogLine {
  playerId: string;
  playerName: string;
  kind: ModerationKind;
  byId: string;
  byName: string;
  at: string;
  reason: string | null;
  until: string | null;
}

/** The admin page (Contracts.cs, AdminOverviewView). */
export interface AdminOverview {
  sanctioned: SanctionedPlayer[];
  recent: ModerationLogLine[];
}

/** A moment, short: "18 Nov 2026, 15:04" in the admin's own time. */
export function when(at: string, format: TimeFormat = {}): string {
  return new Date(at).toLocaleString(format.locale, { dateStyle: "medium", timeStyle: "short", timeZone: format.timeZone });
}

/** A ban or mute in a line: "Banned for good by Ann on 18 Nov 2026, 15:04: Spam". */
export function sanctionLine(what: "Banned" | "Muted", sanction: AdminSanction, format: TimeFormat = {}): string {
  return `${what} ${untilText(sanction.until, format)} by ${sanction.byName} on ${when(sanction.since, format)}: ${sanction.reason}`;
}

/** What Lift does for this player, for its button's name. */
export function liftLabel(player: SanctionedPlayer): string {
  const what = player.ban && player.mute ? "ban and mute" : player.ban ? "ban" : "mute";
  return `Lift ${player.displayName}'s ${what}`;
}

/** A line of the audit trail: who did what to whom, until when, and why. */
export function logLine(line: ModerationLogLine, format: TimeFormat = {}): string {
  const until = untilText(line.until, format);
  const done: Record<ModerationKind, string> = {
    Warned: `${line.byName} warned ${line.playerName}`,
    Muted: `${line.byName} muted ${line.playerName} ${until}`,
    Banned: `${line.byName} banned ${line.playerName} ${until}`,
    MuteLifted: `${line.byName} lifted ${line.playerName}'s mute`,
    BanLifted: `${line.byName} lifted ${line.playerName}'s ban`,
  };
  return `${when(line.at, format)} · ${done[line.kind]}${line.reason ? `: ${line.reason}` : ""}`;
}

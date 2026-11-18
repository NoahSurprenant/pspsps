// The admin page's contents (/admin, admin.html): who is banned or muted now, and what admins did lately
// (src/Pspsps.Server/Moderation/AdminEndpoints.cs, /api/admin/overview).

import { banLengths, doneMessage, muteLengths, requestFor, untilText, type TimeFormat } from "./moderation";
import { reportReasons } from "./safety";

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

/** A line of a report's chat (Contracts.cs, ReportLineView): who said it (their name then), what, when. */
export interface ReportLine {
  playerId: string;
  displayName: string;
  text: string;
  at: string;
}

/** An open report (Contracts.cs, OpenReportView). */
export interface OpenReport {
  id: string;
  reporterId: string;
  reporterName: string;
  reportedId: string;
  reportedName: string;
  /** A ReportReason's name ("MeanChat"...). */
  reason: string;
  note: string | null;
  /** Where, to read: "Town Square, Tabby", or "A home". */
  place: string;
  filedAt: string;
  context: ReportLine[];
}

/** The admin page (Contracts.cs, AdminOverviewView). */
export interface AdminOverview {
  /** The open reports, oldest first. */
  reports: OpenReport[];
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

/** "Tom reported Felix". */
export function reportHeading(report: OpenReport): string {
  return `${report.reporterName} reported ${report.reportedName}`;
}

/** Why, where and when: "Mean or hurtful chat · Town Square, Tabby · 18 Nov 2026, 14:00". */
export function reportFacts(report: OpenReport, format: TimeFormat = {}): string {
  const reason = reportReasons.find((r) => r.reason === report.reason)?.label ?? report.reason;
  return `${reason} · ${report.place} · ${when(report.filedAt, format)}`;
}

/** What an admin can do with a report besides Dismiss: warn, mute or ban the reported cat, for so long. */
export const decisionChoices: readonly { value: string; label: string }[] = [
  { value: "warn", label: "Warn" },
  ...muteLengths.map((length) => ({ value: `mute:${length.value}`, label: `Mute for ${length.label.toLowerCase()}` })),
  ...banLengths.map((length) => ({
    value: `ban:${length.value}`,
    label: length.value === "permanent" ? "Ban for good" : `Ban for ${length.label.toLowerCase()}`,
  })),
];

/** What the server's decide takes, for a choice ("dismiss", or one of decisionChoices); or why not (no reason). */
export function decisionRequest(
  choice: string, reason: string,
): { action: string; reason?: string; length?: string } | string {
  if (choice === "dismiss") {
    return { action: "dismiss" };
  }
  const [action, length] = choice.split(":") as ["warn" | "mute" | "ban", string | undefined];
  return requestFor(action, reason, length);
}

/** What the admin is told once a report is decided. */
export function decidedMessage(choice: string, report: OpenReport): string {
  if (choice === "dismiss") {
    return `${report.reporterName}'s report of ${report.reportedName} is dismissed.`;
  }
  const [action, length] = choice.split(":") as ["warn" | "mute" | "ban", string | undefined];
  return `${doneMessage({ action, reason: "", length }, report.reportedName).replace(/\.$/, "")}; the report is closed.`;
}

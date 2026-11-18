import type { MutedView, ReportReason } from "./types";

/** The players this one has muted, as the server last said: their chat lines and bubbles don't show here. */
export class Mutes {
  private byId = new Map<string, MutedView>();

  /** Everyone muted, by name (the server's order). */
  get list(): MutedView[] {
    return [...this.byId.values()];
  }

  has(playerId: string): boolean {
    return this.byId.has(playerId);
  }

  /**
   * Takes the server's list as it is now: the players muted that weren't before, and whether anything changed (a tab's own
   * change comes back to it from the server too, once as the answer and once as MutesChanged).
   */
  set(list: readonly MutedView[]): { added: string[]; changed: boolean } {
    const before = this.byId;
    this.byId = new Map(list.map((muted) => [muted.playerId, muted]));
    const added = list.map((muted) => muted.playerId).filter((id) => !before.has(id));
    const changed = added.length > 0 || before.size !== this.byId.size
      || list.some((muted) => before.get(muted.playerId)?.displayName !== muted.displayName);
    return { added, changed };
  }
}

/** What a player card's Mute, Unmute and Report buttons do. */
export type SafetyAction = "mute" | "unmute" | "report";

/** The card's quieter buttons, under the friend ones: Mute (or Unmute, for a cat muted already) and Report. */
export function safetyButtons(muted: boolean): { action: SafetyAction; label: string }[] {
  return [
    muted ? { action: "unmute", label: "Unmute" } : { action: "mute", label: "Mute" },
    { action: "report", label: "Report" },
  ];
}

/** What a card says of a cat the player has muted. */
export const mutedStatus = "Muted: you don't see what they say.";

/** The reasons a report can give, as the form lists them (the server takes only these). */
export const reportReasons: readonly { reason: ReportReason; label: string }[] = [
  { reason: "MeanChat", label: "Mean or hurtful chat" },
  { reason: "InappropriateName", label: "Inappropriate name" },
  { reason: "Spam", label: "Spam" },
  { reason: "SomethingElse", label: "Something else" },
];

/** The longest note a report can have (Report.MaxNoteLength on the server). */
export const maxNoteLength = 300;

/** The report form as filled in so far. */
export interface ReportDraft {
  reason: ReportReason | undefined;
  note: string;
  alsoMute: boolean;
}

/** A new report: no reason picked yet, no note, and muting the cat too (unticked by choice). */
export function newReportDraft(): ReportDraft {
  return { reason: undefined, note: "", alsoMute: true };
}

/** What the hub's Report takes. */
export interface ReportRequest {
  reason: ReportReason;
  note: string | null;
  alsoMute: boolean;
}

/** The report to send, or why it can't go yet: a reason picked, and a note (blank is none) no longer than the limit. */
export function reportRequest(draft: ReportDraft): ReportRequest | { problem: string } {
  if (!draft.reason || !reportReasons.some((r) => r.reason === draft.reason)) {
    return { problem: "Pick a reason." };
  }
  const note = draft.note.trim();
  if (note.length > maxNoteLength) {
    return { problem: `A note is at most ${maxNoteLength} characters.` };
  }
  return { reason: draft.reason, note: note === "" ? null : note, alsoMute: draft.alsoMute };
}

/**
 * What the player is told once a report is sent: whether the cat is muted now, and why not when they asked (every mute
 * used: the report went anyway).
 */
export function reportSentMessage(name: string, muted: boolean, askedToMute = false): string {
  if (muted) {
    return `Thanks. Your report was sent, and ${name} is muted.`;
  }
  return askedToMute
    ? `Thanks. Your report was sent. You've muted as many cats as you can, so ${name} isn't muted.`
    : "Thanks. Your report was sent.";
}

/** What the player is told once they mute or unmute a cat. */
export function muteMessage(name: string, muted: boolean): string {
  return muted ? `${name} is muted. Unmute them from their card or Friends.` : `${name} isn't muted any more.`;
}

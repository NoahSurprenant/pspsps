// Moderation as the browser shows it: a banned or muted player's own notice, and an admin's controls on another cat's
// card (src/Pspsps.Server/Moderation, src/Pspsps.Domain/Moderation/PlayerModeration.cs).

/** A ban or mute (Contracts.cs, SanctionView): why, and until when (an ISO time; null: for good). */
export interface Sanction {
  reason: string;
  until: string | null;
}

/** The moderation part of a player card, for admins only (Contracts.cs, CardModerationView). */
export interface CardModeration {
  /** The cat is an admin too: admins aren't moderated (the admin list changes them). */
  isAdmin: boolean;
  ban: Sanction | null;
  mute: Sanction | null;
}

/** What an admin can do to a player, as the hub's Moderate takes it. */
export type ModerationAction = "warn" | "mute" | "ban" | "lift";

/** An admin's action from a card: what, why (shown to the player), and for a mute or ban how long. */
export interface ModerationRequest {
  action: ModerationAction;
  reason: string;
  length?: string;
}

/** How long a mute or ban lasts, as offered (ModerationRequest.MuteLengths and BanLengths on the server). */
export interface LengthChoice {
  value: string;
  label: string;
}

export const muteLengths: readonly LengthChoice[] = [
  { value: "1h", label: "1 hour" },
  { value: "24h", label: "24 hours" },
  { value: "7d", label: "7 days" },
];

export const banLengths: readonly LengthChoice[] = [
  { value: "24h", label: "24 hours" },
  { value: "7d", label: "7 days" },
  { value: "permanent", label: "For good" },
];

/** The longest reason the server keeps (PlayerModeration.MaxReasonLength). */
export const maxReasonLength = 200;

/** What the player sees for an empty reason: the server would refuse it. */
export const sayWhy = "Say why first: the player is shown it.";

/** How a time is shown: in the player's own time zone and language unless told otherwise (tests). */
export interface TimeFormat {
  locale?: string;
  timeZone?: string;
}

/** "until 18 Nov 2026, 15:04" (local time), or "for good". */
export function untilText(until: string | null, format: TimeFormat = {}): string {
  if (until === null) {
    return "for good";
  }
  return `until ${new Date(until).toLocaleString(format.locale, {
    dateStyle: "medium",
    timeStyle: "short",
    timeZone: format.timeZone,
  })}`;
}

/** What a banned player's page says: until when, and why. */
export function bannedLines(ban: Sanction, format: TimeFormat = {}): [string, string] {
  return [`Your cat is banned ${untilText(ban.until, format)}.`, `Why: ${ban.reason}`];
}

/** What a muted player's chat bar says in place of the chat box. */
export function mutedNote(mute: Sanction, format: TimeFormat = {}): string {
  return `You're muted ${untilText(mute.until, format)}. Why: ${mute.reason}`;
}

/** What a newly muted player is told. */
export function mutedToast(mute: Sanction, format: TimeFormat = {}): string {
  return `An admin muted you ${untilText(mute.until, format)}. Why: ${mute.reason}`;
}

/** Whether a mute or ban still holds at <now> (it ends at its time). */
export function inForce(sanction: Sanction | null | undefined, now: Date = new Date()): sanction is Sanction {
  return !!sanction && (sanction.until === null || new Date(sanction.until).getTime() > now.getTime());
}

/** How a cat stands, on an admin's view of its card: its ban and mute, or that it has none. */
export function standingLines(moderation: CardModeration, format: TimeFormat = {}): string[] {
  const lines: string[] = [];
  if (moderation.ban) {
    lines.push(`Banned ${untilText(moderation.ban.until, format)}: ${moderation.ban.reason}`);
  }
  if (moderation.mute) {
    lines.push(`Muted ${untilText(moderation.mute.until, format)}: ${moderation.mute.reason}`);
  }
  return lines.length > 0 ? lines : ["Not banned or muted."];
}

/** Whether there is anything to lift. */
export function canLift(moderation: CardModeration): boolean {
  return moderation.ban !== null || moderation.mute !== null;
}

/**
 * The request a card's button makes, or why not: every action but lifting says why. The reason is trimmed, as the
 * server keeps it.
 */
export function requestFor(action: ModerationAction, reason: string, length?: string): ModerationRequest | string {
  const why = reason.trim();
  if (action !== "lift" && why.length === 0) {
    return sayWhy;
  }
  return { action, reason: why, ...(action === "mute" || action === "ban" ? { length } : {}) };
}

/** What the admin is told once an action is done. */
export function doneMessage(request: ModerationRequest, name: string): string {
  const length = [...muteLengths, ...banLengths].find((choice) => choice.value === request.length)?.label.toLowerCase();
  switch (request.action) {
    case "warn":
      return `${name} was warned.`;
    case "mute":
      return `${name} is muted for ${length ?? "a while"}.`;
    case "ban":
      return request.length === "permanent" ? `${name} is banned for good.` : `${name} is banned for ${length ?? "a while"}.`;
    case "lift":
      return `${name} can play and chat again.`;
  }
}

/**
 * Warnings to show, one at a time, until the player closes each: a dialog closed by a change of view (a room loading)
 * isn't read, and the server has counted the warning as seen once it handed it over.
 */
export class Notices {
  private readonly waiting: string[] = [];

  /** The warning to show now, if any. */
  get current(): string | undefined {
    return this.waiting[0];
  }

  show(text: string): void {
    this.waiting.push(text);
  }

  /** The player closed the one showing. */
  closed(): void {
    this.waiting.shift();
  }
}

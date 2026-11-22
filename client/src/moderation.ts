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
  /** The networks it played from lately (none seen, or network bans off: null). */
  networks?: Network[] | null;
}

/**
 * A network a cat played from lately (Contracts.cs, NetworkView): its key (a keyed hash: what the admin bans it by) and
 * label (the hash's first 8 hex digits: never the address), how many players were on it in the last 7 days, when this
 * cat last was, and its ban in force.
 */
export interface Network {
  key: string;
  label: string;
  players: number;
  lastSeen: string;
  ban: Sanction | null;
  /** Its ban is this cat's (banned with it, lifted with it), not another cat's on the same network. */
  bannedWithIt: boolean;
}

/** What an admin can do to a player, as the hub's Moderate takes it. */
export type ModerationAction = "warn" | "mute" | "ban" | "lift" | "rename";

/** An admin's action from a card: what, why (shown to the player), and for a mute or ban how long. */
export interface ModerationRequest {
  action: ModerationAction;
  reason: string;
  length?: string;
  /** With a ban: the networks (keys) to ban with the cat, and for how long (networkLengths). */
  networks?: string[];
  networkLength?: string;
  /** With a ban: ban only the networks, for a cat banned already (its ban stays as it is). */
  networksOnly?: boolean;
  /** With a lift: whether the cat has a ban or mute to lift (false: only its networks' bans). */
  liftCat?: boolean;
  /** With a lift: lift the bans on the networks banned with the cat too. */
  liftNetworks?: boolean;
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

/** How long a network's ban lasts (ModerationRequest.NetworkLengths): never for good, since a network can be shared. */
export const networkLengths: readonly LengthChoice[] = [
  { value: "24h", label: "24 hours" },
  { value: "7d", label: "7 days" },
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

/** Whether there is anything to lift: the cat's ban or mute, or a ban on one of its networks. */
export function canLift(moderation: CardModeration): boolean {
  return moderation.ban !== null || moderation.mute !== null || bannedNetworks(moderation) > 0;
}

/** How many of the cat's networks are banned now with it (lifted with it: not another cat's network ban). */
export function bannedNetworks(moderation: CardModeration): number {
  return (moderation.networks ?? []).filter((network) => network.ban !== null && network.bannedWithIt).length;
}

/**
 * A network on an admin's view of a card: "Network 1a2b3c4d · 3 cats this week · banned until 20 Nov 2026, 12:00". How many
 * cats share it says whether a ban would catch others (a school, a phone network).
 */
export function networkLine(network: Network, format: TimeFormat = {}): string {
  const cats = network.players === 1 ? "1 cat" : `${network.players} cats`;
  const banned = network.ban ? ` · banned ${untilText(network.ban.until, format)}` : "";
  return `Network ${network.label} · ${cats} this week${banned}`;
}

/**
 * The request a card's button makes, or why not: every action but lifting says why. The reason is trimmed, as the
 * server keeps it.
 */
export function requestFor(
  action: ModerationAction, reason: string, length?: string, options: { networksOnly?: boolean } = {},
): ModerationRequest | string {
  const why = reason.trim();
  // Networks alone, for a cat banned already: they take its ban's reason.
  if (action === "ban" && options.networksOnly) {
    return { action, reason: why, networksOnly: true };
  }
  if (action !== "lift" && why.length === 0) {
    return sayWhy;
  }
  return { action, reason: why, ...(action === "mute" || action === "ban" ? { length } : {}) };
}

/** What the admin is told once a ban is done. */
function banMessage(request: ModerationRequest, name: string, length: string | undefined): string {
  const count = request.networks?.length ?? 0;
  const networkLength = networkLengths.find((choice) => choice.value === request.networkLength)?.label.toLowerCase();
  if (request.networksOnly) {
    const which = count === 1 ? `${name}'s network is` : `${count} of ${name}'s networks are`;
    return `${which} banned for ${networkLength ?? "a while"}.`;
  }
  const banned = request.length === "permanent" ? `${name} is banned for good` : `${name} is banned for ${length ?? "a while"}`;
  if (count === 0) {
    return `${banned}.`;
  }
  const theirs = count === 1 ? "their network" : `${count} of their networks`;
  return `${banned}, and ${theirs} for ${networkLength ?? "a while"}.`;
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
      return banMessage(request, name, length);
    case "lift":
      if (request.liftCat === false) {
        return `${name}'s networks can play again.`;
      }
      return request.liftNetworks
        ? `${name} can play and chat again, and so can their networks.`
        : `${name} can play and chat again.`;
    case "rename":
      return `${name} was renamed to "${request.reason}".`;
  }
}

/** A warning from the admins the player hasn't seen yet (Contracts.cs, WarningView). */
export interface Warning {
  id: string;
  text: string;
}

/**
 * Warnings to show, one at a time, until the player closes each (then the server is told: GameConnection.sawWarning): a
 * dialog closed by a change of view (a room loading) wasn't read. The same warning fetched twice shows once.
 */
export class Notices {
  private readonly waiting: Warning[] = [];
  private readonly seen = new Set<string>();

  /** The warning to show now, if any. */
  get current(): Warning | undefined {
    return this.waiting[0];
  }

  show(warning: Warning): void {
    if (!this.seen.has(warning.id) && !this.waiting.some((w) => w.id === warning.id)) {
      this.waiting.push(warning);
    }
  }

  /** The player closed the one showing: it is seen. */
  closed(): Warning | undefined {
    const warning = this.waiting.shift();
    if (warning) {
      this.seen.add(warning.id);
    }
    return warning;
  }
}

import { el } from "./dom";
import {
  decidedMessage, decisionChoices, decisionRequest, liftLabel, logLine, networkBanLine, networkLiftText, reportFacts,
  reportHeading, sanctionLine, when, type AdminOverview, type NetworkBan, type OpenReport, type SanctionedPlayer,
} from "./admin";
import { maxReasonLength } from "./moderation";
import { roomsSection } from "./roomsAdmin";
import { startTelemetry } from "./telemetry/core";

// Its uncaught errors go to the browser's telemetry, when on. Not its calls' spans or failures: their paths carry player
// and report IDs (/api/admin/players/<id>/...), which telemetry never sends, and a refusal says so on the page. Nor its
// Web Vitals: a few admins' page loads would mix with the players'.
startTelemetry({ performance: false });

// The admin page (/admin): the open reports, oldest first, each with its chat and what to do about it; who is banned or
// muted now, each with Lift; and what admins did lately. Plain HTML, read from
// /api/admin/overview; the server serves this page to admins only.

const root = document.querySelector<HTMLElement>("#ui");
if (!root) {
  throw new Error("admin.html has no #ui");
}

const status = el("p", { className: "status", id: "status" });
status.setAttribute("role", "status");
const content = el("div", { className: "content" });
root.replaceChildren(el("div", { className: "admin" },
  el("header", {},
    el("h1", { text: "Moderation" }),
    link("Back to the game", "/")),
  status,
  content,
  roomsSection(say),
));

void load();

/** Reads the page's lists again, and shows them. */
async function load(): Promise<void> {
  try {
    const response = await fetch("/api/admin/overview");
    if (response.status === 403) {
      content.replaceChildren(el("p", { text: "You're not an admin any more." }));
      return;
    }
    if (!response.ok) {
      throw new Error(`The lists couldn't be read (${response.status}). Reload to try again.`);
    }
    show((await response.json()) as AdminOverview);
  } catch (error) {
    say(error instanceof Error ? error.message : String(error));
  }
}

function show(overview: AdminOverview): void {
  const active = document.activeElement instanceof HTMLElement ? document.activeElement.id : "";
  content.replaceChildren(
    section("reports", "Reports",
      overview.reports.length === 0
        ? el("p", { text: "No reports to look at." })
        : el("ul", { className: "list" }, ...overview.reports.map(openReport))),
    section("sanctioned", "Banned and muted",
      overview.sanctioned.length === 0
        ? el("p", { text: "Nobody is banned or muted." })
        : el("ul", { className: "list" }, ...overview.sanctioned.map(sanctioned))),
    // Shown only with some: most servers never ban a network.
    ...(overview.networkBans.length === 0 ? [] : [section("networks", "Banned networks",
      el("p", { className: "quiet", text: "By a label from a keyed hash: the server never keeps addresses." }),
      el("ul", { className: "list" }, ...overview.networkBans.map(networkBan)))]),
    section("recent", "What admins did lately",
      overview.recent.length === 0
        ? el("p", { text: "Nothing yet." })
        : el("ol", { className: "list log" }, ...overview.recent.map((line) => el("li", { text: logLine(line) })))),
  );
  // Back where it was; or, when that went (a report decided, a ban lifted), at the top of its list.
  const list = /^(act|dismiss|reason|decision)-/.test(active)
    ? "reports-title"
    : active.startsWith("lift-network-") && document.getElementById("networks-title") ? "networks-title" : "sanctioned-title";
  (document.getElementById(active) ?? (active ? document.getElementById(list) : null))?.focus();
}

/**
 * A report: who reported whom, why, where and when, the note, the room's chat then (the reported cat's lines in bold),
 * and Dismiss, or a reason and what to do to the reported cat, which closes the report.
 */
function openReport(report: OpenReport): HTMLElement {
  const chat = report.context.length === 0
    ? el("p", { className: "quiet", text: "No chat with it." })
    : el("ol", { className: "chat" }, ...report.context.map((line) => {
      const who = el(line.playerId === report.reportedId ? "strong" : "span", { text: `${line.displayName}: ` });
      const item = el("li", {}, who, el("span", { text: line.text }));
      item.title = when(line.at);
      return item;
    }));

  const reason = el("input", { id: `reason-${report.id}` });
  reason.maxLength = maxReasonLength;
  reason.autocomplete = "off";
  const reasonLabel = el("label", { text: "Why (the player is shown this)" });
  reasonLabel.htmlFor = reason.id;
  const choice = el("select", { id: `decision-${report.id}` });
  for (const { value, label } of decisionChoices) {
    const option = el("option", { text: label });
    option.value = value;
    choice.append(option);
  }
  const choiceLabel = el("label", { text: "Do" });
  choiceLabel.htmlFor = choice.id;
  choice.setAttribute("aria-label", `What to do about ${report.reportedName}`);

  const act = button("Act", () => void decide(report, choice.value, reason, act));
  act.id = `act-${report.id}`;
  act.setAttribute("aria-label", `Act on ${reportHeading(report)}`);
  const dismiss = button("Dismiss", () => void decide(report, "dismiss", reason, dismiss), "quiet");
  dismiss.id = `dismiss-${report.id}`;
  dismiss.setAttribute("aria-label", `Dismiss ${reportHeading(report)}`);

  return el("li", { className: "report" },
    el("h3", { text: reportHeading(report) }),
    el("p", { text: reportFacts(report) }),
    ...(report.note ? [el("p", { className: "note", text: `"${report.note}"` })] : []),
    chat,
    el("div", { className: "field" }, reasonLabel, reason),
    el("div", { className: "row" }, choiceLabel, choice, act, dismiss),
  );
}

async function decide(report: OpenReport, choice: string, reason: HTMLInputElement, pressed: HTMLButtonElement): Promise<void> {
  const request = decisionRequest(choice, reason.value);
  if (typeof request === "string") {
    say(request);
    reason.focus();
    return;
  }
  pressed.disabled = true;
  try {
    await post(`/api/admin/reports/${encodeURIComponent(report.id)}/decide`, request);
    say(decidedMessage(choice, report));
  } catch (error) {
    say(error instanceof Error ? error.message : String(error));
    pressed.disabled = false;
    return;
  }
  await load();
}

// A change from this page; throws with the server's message when it's refused.
async function post(url: string, body: unknown): Promise<void> {
  const response = await fetch(url, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  if (!response.ok) {
    const refusal = (await response.json().catch(() => null)) as { message?: string } | null;
    throw new Error(refusal?.message ?? `That didn't work (${response.status}).`);
  }
}

function button(text: string, onClick: () => void, className = ""): HTMLButtonElement {
  const element = el("button", { text, className });
  element.type = "button";
  element.addEventListener("click", onClick);
  return element;
}

function sanctioned(player: SanctionedPlayer): HTMLElement {
  const lift = el("button", { text: "Lift", id: `lift-${player.playerId}` });
  lift.type = "button";
  lift.setAttribute("aria-label", liftLabel(player));
  // The networks banned with them: offered with Lift, checked.
  const offer = networkLiftText(player);
  const networks = el("input", { id: `lift-networks-${player.playerId}` });
  networks.type = "checkbox";
  networks.checked = true;
  lift.addEventListener("click", () => void liftFor(player, lift, offer !== undefined && networks.checked));
  return el("li", { className: "player" },
    el("h3", { text: player.displayName }),
    ...(player.ban ? [el("p", { text: sanctionLine("Banned", player.ban) })] : []),
    ...(player.mute ? [el("p", { text: sanctionLine("Muted", player.mute) })] : []),
    el("p", { className: "id" }, el("span", { text: "Player ID " }), el("code", { text: player.playerId })),
    // The whole row is the checkbox (admin.css .check), a single label: big enough to tap, and its font at least
    // 16px so iOS doesn't zoom in on it.
    ...(offer
      ? [el("label", { className: "check" }, networks, el("span", { className: "mark" }), el("span", { text: offer }))]
      : []),
    lift,
  );
}

function networkBan(ban: NetworkBan): HTMLElement {
  const lift = el("button", { text: "Lift", id: `lift-network-${ban.label}` });
  lift.type = "button";
  lift.setAttribute("aria-label", `Lift the ban on ${ban.playerName}'s network ${ban.label}`);
  lift.addEventListener("click", () => void liftNetwork(ban, lift));
  return el("li", { className: "player" }, el("p", { text: networkBanLine(ban) }), lift);
}

async function liftNetwork(ban: NetworkBan, button: HTMLButtonElement): Promise<void> {
  button.disabled = true;
  try {
    await post(`/api/admin/networks/${encodeURIComponent(ban.key)}/lift`, {});
    say(`${ban.playerName}'s network ${ban.label} can play again.`);
  } catch (error) {
    say(error instanceof Error ? error.message : String(error));
    button.disabled = false;
  }
  await load();
}

async function liftFor(player: SanctionedPlayer, button: HTMLButtonElement, liftNetworks: boolean): Promise<void> {
  button.disabled = true;
  try {
    await post(`/api/admin/players/${encodeURIComponent(player.playerId)}/moderate`, { action: "lift", liftNetworks });
    say(liftNetworks
      ? `${player.displayName} can play and chat again, and so can their networks.`
      : `${player.displayName} can play and chat again.`);
  } catch (error) {
    say(error instanceof Error ? error.message : String(error));
    button.disabled = false;
  }
  await load();
}

function say(message: string): void {
  status.textContent = message;
}

function section(id: string, title: string, ...children: Node[]): HTMLElement {
  const heading = el("h2", { text: title, id: `${id}-title` });
  heading.tabIndex = -1;
  const part = el("section", {}, heading, ...children);
  part.setAttribute("aria-labelledby", heading.id);
  return part;
}

function link(text: string, href: string): HTMLAnchorElement {
  const anchor = el("a", { text });
  anchor.href = href;
  return anchor;
}


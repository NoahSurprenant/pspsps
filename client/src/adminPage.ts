import { liftLabel, logLine, sanctionLine, type AdminOverview, type SanctionedPlayer } from "./admin";

// The admin page (/admin): who is banned or muted now, each with Lift, and what admins did lately. Plain HTML, read from
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
    section("sanctioned", "Banned and muted",
      overview.sanctioned.length === 0
        ? el("p", { text: "Nobody is banned or muted." })
        : el("ul", { className: "list" }, ...overview.sanctioned.map(sanctioned))),
    section("recent", "What admins did lately",
      overview.recent.length === 0
        ? el("p", { text: "Nothing yet." })
        : el("ol", { className: "list log" }, ...overview.recent.map((line) => el("li", { text: logLine(line) })))),
  );
  // Back where it was (a Lift that went is gone: its list's heading).
  (document.getElementById(active) ?? (active ? document.getElementById("sanctioned-title") : null))?.focus();
}

function sanctioned(player: SanctionedPlayer): HTMLElement {
  const lift = el("button", { text: "Lift", id: `lift-${player.playerId}` });
  lift.type = "button";
  lift.setAttribute("aria-label", liftLabel(player));
  lift.addEventListener("click", () => void liftFor(player, lift));
  return el("li", { className: "player" },
    el("h3", { text: player.displayName }),
    ...(player.ban ? [el("p", { text: sanctionLine("Banned", player.ban) })] : []),
    ...(player.mute ? [el("p", { text: sanctionLine("Muted", player.mute) })] : []),
    el("p", { className: "id" }, el("span", { text: "Player ID " }), el("code", { text: player.playerId })),
    lift,
  );
}

async function liftFor(player: SanctionedPlayer, button: HTMLButtonElement): Promise<void> {
  button.disabled = true;
  try {
    const response = await fetch(`/api/admin/players/${encodeURIComponent(player.playerId)}/moderate`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ action: "lift" }),
    });
    if (!response.ok) {
      const body = (await response.json().catch(() => null)) as { message?: string } | null;
      throw new Error(body?.message ?? `That didn't work (${response.status}).`);
    }
    say(`${player.displayName} can play and chat again.`);
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

function el<K extends keyof HTMLElementTagNameMap>(
  tag: K, options: { text?: string; className?: string; id?: string } = {}, ...children: Node[]
): HTMLElementTagNameMap[K] {
  const element = document.createElement(tag);
  if (options.text) element.textContent = options.text;
  if (options.className) element.className = options.className;
  if (options.id) element.id = options.id;
  element.append(...children);
  return element;
}

import { emoteBar, emoteForKey } from "./emotes";
import type { Emote, Player, RoomSnapshot, WorldSummary } from "./types";

/** The HTML laid over the canvas: sign-in, the world list, the room bar and the chat box. */
export class Overlay {
  // The room's emote keys (1-4); replaced each time a room shows.
  private emoteKeys = new AbortController();

  constructor(private readonly root: HTMLElement) {}

  showSignIn(providers: string[], signIn: (provider: string) => void, guest: () => void, error?: string): void {
    this.root.replaceChildren(
      panel(
        el("h1", { text: "pspsps" }),
        el("p", { text: "A town full of cats. Come hang out." }),
        ...providers.map((provider) => button(`Sign in with ${provider}`, () => signIn(provider), "primary")),
        button("Play as a guest", guest),
        ...(error ? [el("p", { text: error, className: "error" })] : []),
      ),
    );
  }

  /** This tab stopped: the player opened the game in another one. */
  showReplaced(playHere: () => void): void {
    this.root.replaceChildren(
      panel(
        el("h1", { text: "pspsps" }),
        el("p", { text: "You've joined from another tab." }),
        button("Play here instead", playHere, "primary"),
      ),
    );
  }

  showWorlds(player: Player, worlds: WorldSummary[], enter: (worldId: string) => void, signOut: () => void): void {
    this.root.replaceChildren(
      panel(
        el("h1", { text: `Hi, ${player.displayName}!` }),
        el("p", { text: "Pick a world." }),
        ...worlds.map((world) => {
          const full = world.population >= world.capacity;
          const pick = button(`${world.name} · ${world.population}/${world.capacity}`, () => enter(world.id), "world");
          pick.disabled = full;
          return pick;
        }),
        button("Sign out", signOut, "quiet"),
      ),
    );
  }

  showRoom(
    room: RoomSnapshot, goTo: (roomId: string) => void, say: (text: string) => void, emote: (emote: Emote) => void,
    leave: () => void,
  ): void {
    const input = el("input", { className: "chat" });
    input.maxLength = 120;
    input.placeholder = "Say something…";
    const form = el("form", { className: "chat-bar" }, input, button("Send", () => {}, "primary"));
    form.addEventListener("submit", (event) => {
      event.preventDefault();
      const text = input.value.trim();
      if (text) {
        say(text);
        input.value = "";
      }
    });

    const emotes = el("div", { className: "emote-bar" },
      ...emoteBar.map(({ emote: name, label, key }) => {
        const pick = button(label, () => emote(name));
        pick.title = `${label} (${key})`;
        pick.setAttribute("aria-keyshortcuts", key);
        return pick;
      }),
    );
    this.emoteKeys.abort();
    this.emoteKeys = new AbortController();
    document.addEventListener("keydown", (event) => {
      // Only while this room is showing; never while typing in the chat box.
      const name = emotes.isConnected ? emoteForKey(event) : undefined;
      if (name) {
        event.preventDefault();
        emote(name);
      }
    }, { signal: this.emoteKeys.signal });

    this.root.replaceChildren(
      el("div", { className: "room-bar" },
        el("strong", { text: room.roomName }),
        ...room.rooms.map((link) => button(link.name, () => goTo(link.id))),
        button("Worlds", leave, "quiet"),
      ),
      emotes,
      form,
      el("div", { className: "toast", id: "toast" }),
    );
  }

  toast(message: string): void {
    const toast = this.root.querySelector<HTMLElement>("#toast");
    if (!toast) {
      return;
    }
    toast.textContent = message;
    toast.classList.add("visible");
    window.setTimeout(() => toast.classList.remove("visible"), 4000);
  }
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

function panel(...children: Node[]): HTMLElement {
  return el("div", { className: "panel" }, ...children);
}

function button(text: string, onClick: () => void, className = ""): HTMLButtonElement {
  const element = el("button", { text, className });
  element.type = className === "primary" && text === "Send" ? "submit" : "button";
  if (element.type === "button") {
    element.addEventListener("click", onClick);
  }
  return element;
}

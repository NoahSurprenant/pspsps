import { emoteBar, emoteForKey, isTyping } from "./emotes";
import type { Fishing } from "./fishing";
import type { Emote, Player, RoomSnapshot, WorldSummary } from "./types";

/** A message for the player on a panel; an error shows in red. */
export interface Note {
  text: string;
  error: boolean;
}

/** The providers a guest can save their cat with, and what a button does. */
export interface SaveYourCat {
  providers: string[];
  link: (provider: string) => void;
}

/** What the fishing controls do: walk to the pond and cast, or reel in; and where the line is now. */
export interface FishingControls {
  fish: () => void;
  reel: () => void;
  state: () => Fishing;
}

/** The HTML laid over the canvas: sign-in, the world list, the room bar and the chat box. */
export class Overlay {
  // Listeners that belong to what is showing (the room's emote keys): every show* ends them.
  private showing = new AbortController();
  private fishing: FishingControls | undefined;

  constructor(private readonly root: HTMLElement) {}

  showSignIn(providers: string[], signIn: (provider: string) => void, guest: () => void, note?: Note): void {
    this.replace(
      panel(
        el("h1", { text: "pspsps" }),
        el("p", { text: "A town full of cats. Come hang out." }),
        ...providers.map((provider) => button(`Sign in with ${provider}`, () => signIn(provider), "primary")),
        button("Play as a guest", guest),
        ...noteParagraph(note),
      ),
    );
  }

  /** This tab stopped: the player opened the game in another one. */
  showReplaced(playHere: () => void): void {
    this.replace(
      panel(
        el("h1", { text: "pspsps" }),
        el("p", { text: "You've joined from another tab." }),
        button("Play here instead", playHere, "primary"),
      ),
    );
  }

  /**
   * The world list. A guest also gets "Save your cat with ..." for each provider in `save`, and `note` says how the
   * last try at that went.
   */
  showWorlds(
    player: Player,
    worlds: WorldSummary[],
    enter: (worldId: string) => void,
    signOut: () => void,
    save: SaveYourCat,
    note?: Note,
  ): void {
    const saving = player.isGuest && save.providers.length > 0
      ? [
          el("p", { text: "You're a guest: your cat lasts as long as this browser remembers you." }),
          ...save.providers.map((provider) => button(`Save your cat with ${provider}`, () => save.link(provider), "primary")),
        ]
      : [];
    this.replace(
      panel(
        el("h1", { text: `Hi, ${player.displayName}!` }),
        ...noteParagraph(note),
        ...saving,
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
    leave: () => void, coins: number, fishing?: FishingControls,
  ): void {
    this.fishing = fishing;
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
    this.replace(
      el("div", { className: "room-bar" },
        el("strong", { text: room.roomName }),
        ...room.rooms.map((link) => button(link.name, () => goTo(link.id))),
        el("span", { className: "coins", id: "coins", text: coinText(coins) }),
        button("Worlds", leave, "quiet"),
      ),
      ...(fishing ? [el("div", { className: "fishing", id: "fishing" })] : []),
      emotes,
      form,
      el("div", { className: "toast", id: "toast" }),
    );

    // After replace(), which ends the last view's listeners.
    document.addEventListener("keydown", (event) => {
      // Never while typing in the chat box.
      const name = emoteForKey(event);
      if (name) {
        event.preventDefault();
        emote(name);
      }
    }, { signal: this.showing.signal });

    if (fishing) {
      this.setFishing(fishing.state());
      // Space reels in at a bite (not while typing): faster than reaching for the button. A button with the focus (the
      // last one clicked) would take the same Space as a click of its own on keyup: it loses the focus, and that keyup
      // is swallowed.
      let reeledBySpace = false;
      document.addEventListener("keydown", (event) => {
        if (event.code === "Space" && !event.repeat && !isTyping(event.target) && fishing.state() === "bite") {
          event.preventDefault();
          reeledBySpace = true;
          if (document.activeElement instanceof HTMLElement) {
            document.activeElement.blur();
          }
          fishing.reel();
        }
      }, { signal: this.showing.signal });
      document.addEventListener("keyup", (event) => {
        if (event.code === "Space" && reeledBySpace) {
          event.preventDefault();
          reeledBySpace = false;
        }
      }, { signal: this.showing.signal });
    }
  }

  /** Shows where the line is: a Fish button, casting, waiting for a bite, a bite to reel in now, or reeling in. */
  setFishing(state: Fishing): void {
    const box = this.root.querySelector<HTMLElement>("#fishing");
    const controls = this.fishing;
    if (!box || !controls) {
      return;
    }
    box.dataset.state = state;
    switch (state) {
      case "idle":
        box.replaceChildren(button("🎣 Fish", controls.fish));
        break;
      case "casting":
        box.replaceChildren(el("span", { text: "Casting…" }));
        break;
      case "reeling":
        box.replaceChildren(el("span", { text: "Reeling in…" }));
        break;
      case "waiting":
        box.replaceChildren(el("span", { text: "Waiting for a bite…" }), button("Reel in", controls.reel, "quiet"));
        break;
      case "bite":
        box.replaceChildren(el("strong", { text: "Bite!" }), button("Reel!", controls.reel, "primary reel"));
        break;
    }
  }

  setCoins(coins: number): void {
    const counter = this.root.querySelector<HTMLElement>("#coins");
    if (counter) {
      counter.textContent = coinText(coins);
    }
  }

  /** Shows <children> in place of whatever was showing, and ends the old view's listeners. */
  private replace(...children: Node[]): void {
    this.showing.abort();
    this.showing = new AbortController();
    this.root.replaceChildren(...children);
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

function coinText(coins: number): string {
  return `🪙 ${coins}`;
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

/** A note as a paragraph (an error in red), or nothing. */
function noteParagraph(note: Note | undefined): HTMLElement[] {
  return note ? [el("p", { text: note.text, className: note.error ? "error" : "note" })] : [];
}

import { emoteBar, emoteForKey, isTyping } from "./emotes";
import type { Fishing } from "./fishing";
import { canGoTo, friendButtons, friendStatus, sections, whereIs, type FriendAction, type Here } from "./friends";
import { coatOf } from "./fur";
import type { DoorView, Emote, Friend, Player, PlayerCard, RoomSnapshot, WorldSummary } from "./types";

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
  // The player card or friends list open over the room, if any.
  private dialog: { element: HTMLDialogElement; open: OpenDialog } | undefined;

  /** Opens the friends list: the room bar has a Friends button once this is set. */
  onFriends: (() => void) | undefined;

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
    room: RoomSnapshot, say: (text: string) => void, emote: (emote: Emote) => void, throughDoor: (door: DoorView) => void,
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
        // No buttons to other rooms: the doors are the way around.
        el("strong", { text: room.roomName }),
        el("span", { className: "coins", id: "coins", text: coinText(coins) }),
        ...(this.onFriends ? [button("Friends", this.onFriends)] : []),
        button("Worlds", leave, "quiet"),
      ),
      // The doors are drawn on the canvas, which the keyboard can't reach: a button for each, unseen until it has focus.
      doorLinks(room.doors, throughDoor),
      el("div", { className: "dock" },
        el("div", { className: "toast", id: "toast" }),
        ...(fishing ? [el("div", { className: "fishing", id: "fishing" })] : []),
        emotes,
        form,
      ),
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

  /**
   * Another cat's player card, over the room: its name, its fur, and where the two stand, with the buttons for that
   * (add, cancel, accept or decline, remove). `act` runs a button's change.
   */
  showPlayerCard(card: PlayerCard, act: (action: FriendAction) => void): void {
    const coat = coatOf(card.furColor);
    const swatch = el("span", { className: "swatch" });
    swatch.style.background = `linear-gradient(135deg, ${coat.body} 55%, ${coat.accent} 55%)`;
    swatch.setAttribute("aria-hidden", "true");
    const status = friendStatus(card.friendState);
    this.openDialog({ kind: "card", playerId: card.playerId }, "player-card",
      el("h2", { text: card.displayName, id: "dialog-title" }),
      el("p", { className: "fur" }, swatch, el("span", { text: `${card.furColor} fur` })),
      // What the cat wears goes here once there's a shop.
      ...(status ? [el("p", { text: status, className: "friend-status" })] : []),
      el("div", { className: "actions" },
        ...friendButtons(card.friendState).map((b) => button(b.label, () => act(b.action), b.primary ? "primary" : ""))),
    );
  }

  /**
   * The friends list, over the room: each friend with where they are and "Go to", then requests to the player (accept,
   * decline) and the player's own (cancel). A friend's name opens their card.
   */
  showFriends(
    list: Friend[],
    here: Here | undefined,
    actions: { card: (playerId: string) => void; goTo: (playerId: string) => void; act: (action: FriendAction, playerId: string) => void },
  ): void {
    const { friends, received, sent } = sections(list);
    const name = (friend: Friend) => {
      const open = button(friend.displayName, () => actions.card(friend.playerId), "name");
      open.setAttribute("aria-label", `${friend.displayName}'s card`);
      return open;
    };
    const labelled = (text: string, label: string, onClick: () => void, className = "") => {
      const pick = button(text, onClick, className);
      pick.setAttribute("aria-label", label);
      return pick;
    };
    const row = (friend: Friend, ...children: Node[]) =>
      el("li", { className: "friend" }, name(friend), ...children);

    const parts: Node[] = [el("h2", { text: "Friends", id: "dialog-title" })];
    if (list.length === 0) {
      parts.push(el("p", { text: "No friends yet. Tap a cat to see its card and add it." }));
    }
    if (friends.length > 0) {
      parts.push(el("ul", { className: "friend-list" }, ...friends.map((friend) => {
        const go = labelled("Go to", `Go to ${friend.displayName}`, () => actions.goTo(friend.playerId), "primary");
        go.disabled = !canGoTo(friend, here);
        return row(friend,
          el("span", { className: friend.place ? "where online" : "where", text: whereIs(friend, here) }),
          go);
      })));
    }
    if (received.length > 0) {
      parts.push(el("h3", { text: "Asking you" }), el("ul", { className: "friend-list" }, ...received.map((friend) => row(friend,
        el("span", { className: "actions" },
          labelled("Accept", `Accept ${friend.displayName}`, () => actions.act("accept", friend.playerId), "primary"),
          labelled("Decline", `Decline ${friend.displayName}`, () => actions.act("decline", friend.playerId)))))));
    }
    if (sent.length > 0) {
      parts.push(el("h3", { text: "You asked" }), el("ul", { className: "friend-list" }, ...sent.map((friend) => row(friend,
        labelled("Cancel", `Cancel the request to ${friend.displayName}`, () => actions.act("cancel", friend.playerId))))));
    }
    this.openDialog({ kind: "friends" }, "friends", ...parts);
  }

  /** What is open over the room: a player card (whose), the friends list, or nothing. */
  get openDialogFor(): OpenDialog | undefined {
    return this.dialog?.element.open ? this.dialog.open : undefined;
  }

  closeDialog(): void {
    const dialog = this.dialog;
    this.dialog = undefined;
    if (dialog?.element.open) {
      dialog.element.close();
    }
    dialog?.element.remove();
  }

  // A modal dialog over the room (the page behind it inert until it closes), replacing any open one. Escape or Close
  // closes it. The same one open already (the list, or the same cat's card) is brought up to date in place instead.
  private openDialog(open: OpenDialog, className: string, ...children: Node[]): void {
    const current = this.dialog;
    if (current?.element.open && sameDialog(current.open, open)) {
      refreshInPlace(current.element, children);
      return;
    }
    this.closeDialog();
    const dialog = el("dialog", { className: `sheet ${className}` },
      ...children,
      el("p", { className: "dialog-message", id: "dialog-message" }),
      button("Close", () => this.closeDialog(), "quiet"),
    );
    dialog.setAttribute("aria-labelledby", "dialog-title");
    dialog.addEventListener("close", () => {
      if (this.dialog?.element === dialog) {
        this.closeDialog();
      }
    });
    // A tap on the backdrop (outside the sheet) closes it too.
    dialog.addEventListener("click", (event) => {
      if (event.target === dialog) {
        const r = dialog.getBoundingClientRect();
        if (event.clientX < r.left || event.clientX > r.right || event.clientY < r.top || event.clientY > r.bottom) {
          this.closeDialog();
        }
      }
    });
    this.root.append(dialog);
    this.dialog = { element: dialog, open };
    dialog.showModal();
  }

  /** Shows <children> in place of whatever was showing, and ends the old view's listeners. */
  private replace(...children: Node[]): void {
    this.closeDialog();
    this.showing.abort();
    this.showing = new AbortController();
    this.root.replaceChildren(...children);
  }

  toast(message: string): void {
    // Said in the open card or list too: the toast is behind it.
    const inDialog = this.dialog?.element.querySelector<HTMLElement>("#dialog-message");
    if (inDialog) {
      inDialog.textContent = message;
    }
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

/** A button through each door, for the keyboard: the doors themselves are drawn on the canvas. */
function doorLinks(doors: DoorView[], throughDoor: (door: DoorView) => void): HTMLElement {
  const nav = el("nav", { className: "doors" },
    ...doors.map((door) => button(`Through the door to ${door.toRoomName}`, () => throughDoor(door))),
  );
  nav.setAttribute("aria-label", "Doors");
  return nav;
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

/** What a dialog over the room shows: a player's card, or the friends list. */
export type OpenDialog = { kind: "card"; playerId: string } | { kind: "friends" };

function sameDialog(a: OpenDialog, b: OpenDialog): boolean {
  return a.kind === "friends" ? b.kind === "friends" : b.kind === "card" && a.playerId === b.playerId;
}

/**
 * Swaps a dialog's contents (all but its message and Close) for `children`, keeping where the player was: the button
 * with the focus (found again by its name) and the scroll.
 */
function refreshInPlace(dialog: HTMLDialogElement, children: Node[]): void {
  const focused = document.activeElement instanceof HTMLElement && dialog.contains(document.activeElement)
    ? nameOf(document.activeElement)
    : undefined;
  const scroll = dialog.scrollTop;
  const kept = [...dialog.children].slice(-2);
  dialog.replaceChildren(...children, ...kept);
  dialog.scrollTop = scroll;
  if (focused !== undefined) {
    const again = [...dialog.querySelectorAll<HTMLElement>("button")].find((b) => nameOf(b) === focused);
    (again ?? dialog.querySelector<HTMLElement>("button"))?.focus();
  }
}

function nameOf(element: HTMLElement): string {
  return element.getAttribute("aria-label") ?? element.textContent ?? "";
}

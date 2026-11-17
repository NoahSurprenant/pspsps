import { emoteBar, emoteForKey, isTyping } from "./emotes";
import type { Fishing } from "./fishing";
import { canGoTo, friendButtons, friendStatus, sections, whereIs, type FriendAction, type Here } from "./friends";
import { coatOf } from "./fur";
import { visitorChoices, type FurnitureLeft } from "./home";
import { shopSections, wearingLine, type ShopAction, type ShopRow } from "./shop";
import type {
  DoorView, Emote, FurColor, Friend, FurnitureView, HomeVisitors, Player, PlayerCard, RoomSnapshot, Shop, WorldSummary,
} from "./types";

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

/** What the owner's controls in their own home do: decorate, and choose who may visit (now `visitors`). */
export interface HomeControls {
  decorate: () => void;
  visitors: HomeVisitors;
  letIn: (visitors: HomeVisitors) => void;
}

/**
 * The decorating panel's contents: what to do next, the owner's furniture with how many of each are left to put out,
 * the pieces out in the home, the one picked to put out, and the piece selected.
 */
export interface DecorateView {
  hint: string;
  left: readonly FurnitureLeft[];
  placed: readonly FurnitureView[];
  picked?: string;
  selected?: FurnitureView;
}

/** What the decorating panel's buttons do. */
export interface DecorateActions {
  /** Picks a piece of furniture to put out (a tap on the floor puts it there), or drops the pick when it's picked. */
  pick: (itemId: string) => void;
  /** Puts the picked furniture out wherever it fits: for the keyboard, which can't tap the floor. */
  putAnywhere: (itemId: string) => void;
  /** Selects a piece out in the home (or drops the selection when it's selected). */
  select: (pieceId: string) => void;
  turn: (pieceId: string) => void;
  putAway: (pieceId: string) => void;
  done: () => void;
}

/** The HTML laid over the canvas: sign-in, the world list, the room bar and the chat box. */
export class Overlay {
  // Listeners that belong to what is showing (the room's emote keys): every show* ends them.
  private showing = new AbortController();
  // Listeners of the decorating panel, while it shows.
  private decorating: AbortController | undefined;
  private fishing: FishingControls | undefined;
  // The player card or friends list open over the room, if any.
  private dialog: { element: HTMLDialogElement; open: OpenDialog } | undefined;

  /** Opens the friends list: the room bar has a Friends button once this is set. */
  onFriends: (() => void) | undefined;

  /** Opens the shop: the room bar has a Shop button once this is set. */
  onShop: (() => void) | undefined;

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
    leave: () => void, coins: number, fishing?: FishingControls, home?: HomeControls,
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
        ...(this.onShop ? [button("Shop", this.onShop)] : []),
        ...(this.onFriends ? [button("Friends", this.onFriends)] : []),
        button("Worlds", leave, "quiet"),
      ),
      // The doors are drawn on the canvas, which the keyboard can't reach: a button for each, unseen until it has focus.
      doorLinks(room.doors, throughDoor),
      el("div", { className: "dock" },
        el("div", { className: "toast", id: "toast" }),
        ...(fishing ? [el("div", { className: "fishing", id: "fishing" })] : []),
        ...(home ? [homeTools(home)] : []),
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

  /** Shows who may visit the owner's home, as it is now (changed from another tab, say). */
  setVisitors(visitors: HomeVisitors): void {
    const choice = this.root.querySelector<HTMLSelectElement>("#home-visitors");
    if (choice) {
      choice.value = visitors;
    }
  }

  /** Whether the decorating panel is showing. */
  get isDecorating(): boolean {
    return this.decorating !== undefined;
  }

  /**
   * The decorating panel, in place of the emotes and the chat at the bottom of the screen, the room still showing (and
   * taking taps) above it: the owner's furniture to pick and put out, the pieces out to select, and Turn and Put away
   * for the one selected. Shown again in place as things change, keeping the focus where it was. Escape or Done ends it.
   */
  showDecorating(view: DecorateView, actions: DecorateActions): void {
    const dock = this.root.querySelector<HTMLElement>(".dock");
    if (!dock) {
      return;
    }
    const panel = dock.querySelector<HTMLElement>("#decorate") ?? this.openDecorating(dock, actions);
    const active = document.activeElement instanceof HTMLElement && panel.contains(document.activeElement)
      ? document.activeElement.dataset.focusKey ?? "heading"
      : undefined;

    const keyed = (text: string, key: string, onClick: () => void, className = "", label?: string) => {
      const pick = button(text, onClick, className);
      pick.dataset.focusKey = key;
      if (label) {
        pick.setAttribute("aria-label", label);
      }
      return pick;
    };
    const heading = el("h2", { text: "Decorating", id: "decorate-title" });
    heading.tabIndex = -1;
    const parts: Node[] = [
      el("div", { className: "decorate-head" }, heading, keyed("Done", "done", actions.done, "primary", "Done decorating")),
      el("p", { className: "decorate-hint", id: "decorate-hint", text: view.hint }),
    ];
    const selected = view.selected;
    const picked = view.left.find((row) => row.item.id === view.picked);
    if (selected) {
      parts.push(el("div", { className: "actions" },
        keyed("Turn", "turn", () => actions.turn(selected.id), "", `Turn the ${selected.name}`),
        keyed("Put away", "put-away", () => actions.putAway(selected.id), "", `Put the ${selected.name} away`)));
    } else if (picked) {
      parts.push(el("div", { className: "actions" },
        keyed("Find it a spot", "anywhere", () => actions.putAnywhere(picked.item.id), "",
          `Put the ${picked.item.name} wherever it fits`)));
    }
    if (view.left.length > 0) {
      parts.push(el("h3", { text: "Your furniture" }), el("ul", { className: "furniture-list" },
        ...view.left.map((row) => {
          const pick = keyed(`${row.item.name} · ${row.left} left`, `item-${row.item.id}`, () => actions.pick(row.item.id));
          pick.setAttribute("aria-pressed", String(row.item.id === view.picked));
          pick.disabled = row.left === 0;
          return el("li", {}, pick);
        })));
    }
    if (view.placed.length > 0) {
      parts.push(el("h3", { text: "In your home" }), el("ul", { className: "furniture-list" },
        ...view.placed.map((piece, i) => {
          const pick = keyed(piece.name, `piece-${piece.id}`, () => actions.select(piece.id), "",
            `${piece.name}, ${i + 1} of ${view.placed.length}`);
          pick.setAttribute("aria-pressed", String(piece.id === selected?.id));
          return el("li", {}, pick);
        })));
    }
    panel.replaceChildren(...parts);

    const again = [...panel.querySelectorAll<HTMLButtonElement>("button")].find((b) => b.dataset.focusKey === active && !b.disabled);
    if (again) {
      again.focus();
    } else if (active !== undefined) {
      // What had the focus went (a piece put away, the last one put out): the panel keeps it, at its heading.
      heading.focus();
    }
  }

  /** Ends the decorating panel: the emotes and the chat come back. */
  stopDecorating(): void {
    if (!this.decorating) {
      return;
    }
    this.decorating.abort();
    this.decorating = undefined;
    const dock = this.root.querySelector<HTMLElement>(".dock");
    dock?.querySelector("#decorate")?.remove();
    for (const part of dock?.querySelectorAll<HTMLElement>(":scope > [data-decorating-hides]") ?? []) {
      part.hidden = false;
      delete part.dataset.decoratingHides;
    }
    this.root.querySelector<HTMLElement>("#home-tools button")?.focus();
  }

  // Makes the decorating panel in the dock, in place of what the dock showed (but its toast), with the focus on it.
  private openDecorating(dock: HTMLElement, actions: DecorateActions): HTMLElement {
    for (const part of dock.querySelectorAll<HTMLElement>(":scope > :not(.toast)")) {
      part.hidden = true;
      part.dataset.decoratingHides = "";
    }
    const panel = el("section", { className: "decorate", id: "decorate" });
    panel.setAttribute("aria-labelledby", "decorate-title");
    dock.append(panel);
    this.decorating = new AbortController();
    document.addEventListener("keydown", (event) => {
      if (event.key === "Escape" && !this.openDialogFor) {
        event.preventDefault();
        actions.done();
      }
    }, { signal: this.decorating.signal });
    // The focus moves from the Decorate button, now hidden, to the panel: shown as its heading, once it has one.
    const placeholder = el("span", {});
    placeholder.dataset.focusKey = "heading";
    placeholder.tabIndex = -1;
    panel.append(placeholder);
    placeholder.focus();
    return panel;
  }

  /**
   * Another cat's player card, over the room: its name, its fur, and where the two stand, with the buttons for that
   * (add, cancel, accept or decline, remove), and "Visit home" when its home would let the player in. `act` runs a
   * button's change.
   */
  showPlayerCard(card: PlayerCard, act: (action: FriendAction) => void, visitHome?: () => void): void {
    const visit = card.canVisitHome && visitHome ? [button("Visit home", visitHome)] : [];
    const swatch = swatchOf(card.furColor);
    const status = friendStatus(card.friendState);
    const wearing = wearingLine(card);
    this.openDialog({ kind: "card", playerId: card.playerId }, "player-card",
      el("h2", { text: card.displayName, id: "dialog-title" }),
      el("p", { className: "fur" }, swatch, el("span", { text: `${card.furColor} fur` })),
      ...(wearing ? [el("p", { text: wearing, className: "wearing" })] : []),
      ...(status ? [el("p", { text: status, className: "friend-status" })] : []),
      el("div", { className: "actions" },
        ...friendButtons(card.friendState).map((b) => button(b.label, () => act(b.action), b.primary ? "primary" : "")),
        ...visit),
    );
  }

  /**
   * The friends list, over the room: the way home first ("Go home", unless the player is home), then each friend with
   * where they are, "Go to" and, when their home lets the player in, "Visit home"; then requests to the player (accept,
   * decline) and the player's own (cancel). A friend's name opens their card.
   */
  showFriends(
    list: Friend[],
    here: Here | undefined,
    actions: {
      card: (playerId: string) => void;
      goTo: (playerId: string) => void;
      act: (action: FriendAction, playerId: string) => void;
      goHome: () => void;
      visitHome: (playerId: string) => void;
    },
    atHome = false,
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

    // The player's own home lives here, with the homes of their friends, rather than in the room bar, which a folded
    // phone has no room left in.
    const parts: Node[] = [
      el("h2", { text: "Friends", id: "dialog-title" }),
      el("div", { className: "home-row" },
        atHome
          ? el("p", { text: "You're home." })
          : labelled("Go home", "Go to your home", actions.goHome, "primary")),
    ];
    if (list.length === 0) {
      parts.push(el("p", { text: "No friends yet. Tap a cat to see its card and add it." }));
    }
    if (friends.length > 0) {
      parts.push(el("ul", { className: "friend-list" }, ...friends.map((friend) => {
        const go = labelled("Go to", `Go to ${friend.displayName}`, () => actions.goTo(friend.playerId), "primary");
        go.disabled = !canGoTo(friend, here);
        const inTheirHome = here?.homeOwnerId === friend.playerId;
        const visit = friend.canVisitHome && !inTheirHome
          ? [labelled("Visit home", `Visit ${friend.displayName}'s home`, () => actions.visitHome(friend.playerId))]
          : [];
        return row(friend,
          el("span", { className: friend.place ? "where online" : "where", text: whereIs(friend, here) }),
          el("span", { className: "friend-buttons" }, go, ...visit));
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

  /**
   * The shop, over the room: the player's coins, then the hats, collars and furs, each with its price and Buy (disabled,
   * with the coins still needed, when the player can't afford it), or Wear or Take off for what they have. `act` runs
   * a button's change.
   */
  showShop(shop: Shop, act: (action: ShopAction, itemId: string) => void): void {
    // The shop opens at its top, on its heading: the first button can be far down (a new guest's is Close).
    const heading = el("h2", { text: "Shop", id: "dialog-title" });
    heading.tabIndex = -1;
    heading.autofocus = true;
    const parts: Node[] = [
      heading,
      el("p", { className: "purse", text: `You have ${coinText(shop.coins)}` }),
    ];
    for (const section of shopSections(shop)) {
      parts.push(
        el("h3", { text: section.title }),
        el("ul", { className: "shop-list" }, ...section.rows.map((row) => shopItem(row, act))),
      );
    }
    this.openDialog({ kind: "shop" }, "shop", ...parts);
  }

  /** What is open over the room: a player card (whose), the friends list, the shop, or nothing. */
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
    this.decorating?.abort();
    this.decorating = undefined;
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

/** One item in the shop: a fur's colour, its name, its price or whether it's worn, and its button. */
function shopItem(row: ShopRow, act: (action: ShopAction, itemId: string) => void): HTMLElement {
  const { item } = row;
  const status = el("span", { className: "item-status", id: `item-${item.id}`, text: row.reason ? `${row.status} · ${row.reason}` : row.status });
  const children: Node[] = [];
  if (item.furColor) {
    children.push(swatchOf(item.furColor));
  }
  children.push(el("span", { className: "item-name", text: item.name }), status);
  const action = row.button;
  if (action) {
    const pick = button(action.label, () => act(action.action, item.id), action.primary ? "primary" : "");
    pick.setAttribute("aria-label", action.ariaLabel);
    // Its price, and why it can't be bought yet.
    pick.setAttribute("aria-describedby", status.id);
    pick.disabled = action.disabled;
    // Found again by its item when the shop is brought up to date (Buy becomes Wear): the focus stays on its row.
    pick.dataset.focusKey = item.id;
    children.push(pick);
  }
  return el("li", { className: "shop-item" }, ...children);
}

/** A fur colour as a small round swatch: its body colour, and its accent. */
function swatchOf(fur: FurColor): HTMLElement {
  const coat = coatOf(fur);
  const swatch = el("span", { className: "swatch" });
  swatch.style.background = `linear-gradient(135deg, ${coat.body} 55%, ${coat.accent} 55%)`;
  swatch.setAttribute("aria-hidden", "true");
  return swatch;
}

/** The owner's controls in their own home: Decorate, and who can visit. */
function homeTools(home: HomeControls): HTMLElement {
  const choice = el("select", { id: "home-visitors" });
  for (const { value, label } of visitorChoices) {
    const option = el("option", { text: label });
    option.value = value;
    choice.append(option);
  }
  choice.value = home.visitors;
  choice.addEventListener("change", () => home.letIn(choice.value as HomeVisitors));
  const label = el("label", { text: "Who can visit" });
  label.htmlFor = choice.id;
  return el("div", { className: "home-tools", id: "home-tools" },
    button("Decorate", home.decorate, "primary"),
    el("span", { className: "visitors" }, label, choice),
  );
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

/** What a dialog over the room shows: a player's card, the friends list, or the shop. */
export type OpenDialog = { kind: "card"; playerId: string } | { kind: "friends" } | { kind: "shop" };

function sameDialog(a: OpenDialog, b: OpenDialog): boolean {
  return a.kind === "card" ? b.kind === "card" && a.playerId === b.playerId : a.kind === b.kind;
}

/**
 * Swaps a dialog's contents (all but its message and Close) for `children`, keeping where the player was: the button
 * with the focus (found again by its focus key, the shop's item, or else by its name) and the scroll.
 */
function refreshInPlace(dialog: HTMLDialogElement, children: Node[]): void {
  const active = document.activeElement instanceof HTMLElement && dialog.contains(document.activeElement)
    ? document.activeElement
    : undefined;
  const key = active?.dataset.focusKey;
  const focused = active && nameOf(active);
  const scroll = dialog.scrollTop;
  const kept = [...dialog.children].slice(-2);
  dialog.replaceChildren(...children, ...kept);
  dialog.scrollTop = scroll;
  if (active?.id && !(active instanceof HTMLButtonElement)) {
    // Not a button (the shop's heading): the same element again.
    dialog.querySelector<HTMLElement>(`#${CSS.escape(active.id)}`)?.focus();
  } else if (focused !== undefined) {
    const buttons = [...dialog.querySelectorAll<HTMLElement>("button")];
    const again = (key !== undefined ? buttons.find((b) => b.dataset.focusKey === key && !isDisabled(b)) : undefined)
      ?? buttons.find((b) => nameOf(b) === focused && !isDisabled(b));
    (again ?? dialog.querySelector<HTMLElement>("button:not(:disabled)"))?.focus();
  }
}

function isDisabled(element: HTMLElement): boolean {
  return element instanceof HTMLButtonElement && element.disabled;
}

function nameOf(element: HTMLElement): string {
  return element.getAttribute("aria-label") ?? element.textContent ?? "";
}

import { el } from "./dom";
import { FRAME_RATES, type FrameRate } from "./frameLimit";
import { hasDoorway } from "./doors";
import { emoteBar, emoteForKey, isTyping } from "./emotes";
import type { Fishing } from "./fishing";
import { canGoTo, friendButtons, friendStatus, sections, whereIs, type FriendAction, type Here } from "./friends";
import { coatOf } from "./fur";
import { visitorChoices, type FurnitureLeft } from "./home";
import { activityName } from "./roomActivities";
import { drawMap } from "./worldMap";
import {
  banLengths, bannedLines, bannedNetworks, canLift, maxReasonLength, mutedNote, muteLengths, networkLengths, networkLine,
  requestFor, standingLines,
  type CardModeration, type LengthChoice, type ModerationAction, type ModerationRequest, type Sanction,
} from "./moderation";
import {
  maxNoteLength, mutedStatus, newReportDraft, reportReasons, safetyButtons, type ReportDraft, type SafetyAction,
} from "./safety";
import {
  catalogueSections, hasMoreToWear, wardrobeSections, wearingLine, type ShopAction, type ShopRow, type ShopSection,
} from "./shop";
import { maxDisplayNameLength, type OwnCardRename } from "./rename";
import { pageTitle } from "./title";
import type {
  DoorView, PropView, Emote, FurColor, Friend, FurnitureView, HomeVisitors, MutedView, Player, PlayerCard, RoomSnapshot, Shop,
  WorldSummary,
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

/** A picture of a cat for its card: what it shows (for a screen reader), and the picture, now or once it is taken. */
export interface CatPicture {
  description: string;
  src: string | Promise<string> | undefined;
}

/** The room's buttons for the keyboard (the room itself is a canvas): the player's own cat, and the catalogue in the shop. */
export interface RoomKeys {
  yourCat: () => void;
  catalogue?: () => void;
  activity: (prop: PropView) => void;
  travel: (roomId: string) => void;
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
  private dialog: { element: HTMLDialogElement; open: OpenDialog; closed?: () => void } | undefined;

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

  /** An admin banned the player: why and until when, and nothing to press (they can't play until it ends). */
  showBanned(ban: Sanction): void {
    const [until, why] = bannedLines(ban);
    this.replace(
      panel(
        el("h1", { text: "pspsps" }),
        el("p", { text: until, className: "banned" }),
        el("p", { text: why }),
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
    frame?: FrameRateChoice,
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
        ...(frame ? [frameRateChooser(frame)] : []),
        button("Sign out", signOut, "quiet"),
      ),
    );
  }

  showRoom(
    room: RoomSnapshot, say: (text: string) => void, emote: (emote: Emote) => void, throughDoor: (door: DoorView) => void,
    leave: () => void, coins: number, keys: RoomKeys, fishing?: FishingControls, home?: HomeControls,
  ): void {
    this.fishing = fishing;
    const input = el("input", { className: "chat" });
    input.maxLength = 120;
    input.placeholder = "Say something…";
    // A muted player sees why and until when in place of the chat box (setChatMute).
    const muted = el("p", { className: "muted-note", id: "muted-note" });
    muted.hidden = true;
    const form = el("form", { className: "chat-bar" }, muted, input, button("Send", () => {}, "primary"));
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
    const map = button("", () => this.showMap(room, keys.travel), "map-toggle");
    map.setAttribute("aria-label", "World map");
    map.setAttribute("aria-haspopup", "dialog");
    this.replace(
      // Small, to leave the room in view: the coins, friends, world map and world picker.
      el("div", { className: "room-bar" },
        el("span", { className: "coins", id: "coins", text: coinText(coins) }),
        ...(this.onFriends ? [button("Friends", this.onFriends)] : []),
        ...(room.rooms.length >= 2 ? [map] : []),
        button("Worlds", leave, "quiet"),
      ),
      // The cat, the doors and the catalogue are drawn on the canvas, which the keyboard can't reach: a button for each,
      // unseen until it has focus.
      keyboardLinks(room, throughDoor, keys),
      el("div", { className: "dock" },
        el("div", { className: "toast", id: "toast" }),
        ...(fishing ? [el("div", { className: "fishing", id: "fishing" })] : []),
        ...(home ? [homeTools(home)] : []),
        emotes,
        form,
      ),
    );

    document.title = pageTitle(room.roomName);

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

  /** Turns the chat box off while the player is muted, saying why and until when; on again with null. */
  setChatMute(mute: Sanction | null): void {
    const form = this.root.querySelector<HTMLFormElement>(".chat-bar");
    const note = form?.querySelector<HTMLElement>("#muted-note");
    if (!form || !note) {
      return;
    }
    note.textContent = mute ? mutedNote(mute) : "";
    note.hidden = !mute;
    for (const control of form.querySelectorAll<HTMLElement>("input, button")) {
      control.hidden = !!mute;
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
        keyed("Put away", "put-away", () => actions.putAway(selected.id), "", `Put away the ${selected.name}`)));
    } else if (picked) {
      parts.push(el("div", { className: "actions" },
        keyed("Find a spot", "anywhere", () => actions.putAnywhere(picked.item.id), "", `Find a spot for the ${picked.item.name}`)));
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
      // What had the focus went (a piece put away, the last one put out), or the panel just opened: at its heading.
      panel.scrollTop = 0;
      heading.focus();
    } else {
      // Changed from the room (a tap on the floor or a piece): at its top, where the hint and Turn and Put away are, not
      // scrolled down where the last button pressed was (on a short screen, its heading and Done were off the top).
      panel.scrollTop = 0;
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
   * Another cat's player card, over the room: its name, a picture of the cat as it looks now, its fur, and where the
   * two stand, with the buttons for that (add, cancel, accept or decline, remove), and "Visit home" when <visitHome> is
   * given (main.ts decides when). `act` runs a button's change. Under them, quieter, Mute (or Unmute, when `muted`) and
   * Report, which `safety` runs.
   */
  showPlayerCard(
    card: PlayerCard, picture: CatPicture, act: (action: FriendAction) => void, visitHome: (() => void) | undefined,
    muted: boolean, safety: (action: SafetyAction) => void, moderate?: (request: ModerationRequest) => void,
  ): void {
    const visit = visitHome ? [button("Visit home", visitHome)] : [];
    const swatch = swatchOf(card.furColor);
    const status = friendStatus(card.friendState);
    const wearing = wearingLine(card);
    // An admin's Moderate section, kept open (and its reason kept) when the same card is brought up to date.
    const open = this.openDialogFor;
    const before = open?.kind === "card" && open.playerId === card.playerId
      ? this.dialog?.element.querySelector<HTMLDetailsElement>("details.moderate")
      : undefined;
    const moderation = card.moderation && moderate
      ? [moderateSection(card.displayName, card.moderation, moderate, (message) => this.say(message), before ?? undefined)]
      : [];
    this.openDialog({ kind: "card", playerId: card.playerId }, "player-card",
      el("h2", { text: card.displayName, id: "dialog-title" }),
      portrait(picture),
      el("p", { className: "fur" }, swatch, el("span", { text: `${card.furColor} fur` })),
      ...(wearing ? [el("p", { text: wearing, className: "wearing" })] : []),
      ...(status ? [el("p", { text: status, className: "friend-status" })] : []),
      el("div", { className: "actions" },
        ...friendButtons(card.friendState).map((b) => button(b.label, () => act(b.action), b.primary ? "primary" : "")),
        ...visit),
      ...(muted ? [el("p", { text: mutedStatus, className: "muted-status" })] : []),
      el("div", { className: "actions safety" },
        ...safetyButtons(muted).map((b) => {
          const pick = button(b.label, () => safety(b.action), "quiet");
          pick.setAttribute("aria-label", `${b.label} ${card.displayName}`);
          return pick;
        })),
      ...moderation,
    );
  }

  /** A word from the admins, over whatever shows: the player reads it, then closes it. */
  showNotice(title: string, text: string, closed: () => void): void {
    this.openDialog({ kind: "notice" }, "notice", openingHeading(title), el("p", { text, className: "notice-text" }));
    if (this.dialog) {
      this.dialog.closed = closed;
    }
  }

  // Says <message> in the open dialog (its message line), as the toast would behind it.
  private say(message: string): void {
    const line = this.dialog?.element.querySelector<HTMLElement>("#dialog-message");
    if (line) {
      line.textContent = message;
    }
  }

  /**
   * The report form for another cat, over the room (from its card): a reason from the list, an optional note, and "Also
   * mute them" (ticked to begin with). `send` takes the form as filled in; `back` goes back to the card.
   */
  showReport(playerId: string, name: string, send: (draft: ReportDraft) => void, back: () => void): void {
    const draft = newReportDraft();
    const reasons = el("fieldset", { className: "report-reasons" }, el("legend", { text: "What's wrong?" }),
      ...reportReasons.map(({ reason, label }) => {
        const pick = el("input");
        pick.type = "radio";
        pick.name = "report-reason";
        pick.value = reason;
        pick.addEventListener("change", () => (draft.reason = reason));
        return choice(pick, label);
      }));
    const note = el("textarea", { id: "report-note" });
    note.maxLength = maxNoteLength;
    note.rows = 3;
    note.addEventListener("input", () => (draft.note = note.value));
    const noteLabel = el("label", { text: "Anything to add? (optional)" });
    noteLabel.htmlFor = note.id;
    const mute = el("input");
    mute.type = "checkbox";
    mute.checked = draft.alsoMute;
    mute.addEventListener("change", () => (draft.alsoMute = mute.checked));
    const form = el("form", { className: "report-form" },
      el("p", { text: "If they're in this room or said something in it, the last things said here go with your report, for the people who keep the game friendly." }),
      reasons,
      el("div", { className: "report-note" }, noteLabel, note),
      choice(mute, "Also mute them"),
      el("div", { className: "actions" }, button("Send report", () => {}, "primary", "submit"), button("Back", back)),
    );
    form.addEventListener("submit", (event) => {
      event.preventDefault();
      send({ ...draft });
    });
    this.openDialog({ kind: "report", playerId }, "player-card report", openingHeading(`Report ${name}`), form);
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
      unmute: (playerId: string) => void;
    },
    atHome = false,
    muted: readonly MutedView[] = [],
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
          : button("Go home", actions.goHome, "primary")),
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
          ? [labelled("Visit home", `Visit home of ${friend.displayName}`, () => actions.visitHome(friend.playerId))]
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
    // Whom the player has muted, to undo it: here, where people are listed, rather than another button in the room.
    if (muted.length > 0) {
      parts.push(el("h3", { text: "Muted" }), el("ul", { className: "friend-list muted-list" }, ...muted.map((cat) =>
        el("li", { className: "friend" },
          el("span", { className: "name", text: cat.displayName }),
          labelled("Unmute", `Unmute ${cat.displayName}`, () => actions.unmute(cat.playerId))))));
    }
    this.openDialog({ kind: "friends" }, "friends", ...parts);
  }

  /**
   * The shop's catalogue, over the room (opened at its stand, in the shop): the player's coins, then the hats, collars,
   * furs and furniture, each with its price and Buy (disabled, with the coins still needed, when the player can't afford
   * it); what they have already says so. `act` runs a button's change.
   */
  showCatalogue(shop: Shop, act: (action: ShopAction, itemId: string) => void): void {
    this.openDialog({ kind: "catalogue" }, "catalogue",
      openingHeading("Catalogue"),
      el("p", { className: "catalogue-blurb", text: "Hats, collars, coats and things for your home." }),
      el("p", { className: "purse", text: `You have ${coinText(shop.coins)}` }),
      ...sectionsOf(catalogueSections(shop), act),
    );
  }

  /**
   * The player's own card, over the room (a tap on their own cat): their name with a small Rename control (`rename`,
   * which opens an inline text box with Save and Cancel), a picture of their cat as it looks now, what it wears, and
   * everything they have to wear, with Wear or Take off (a fur is changed, not taken off). `act` runs a button's change.
   */
  showOwnCard(
    name: string, shop: Shop, picture: CatPicture, act: (action: ShopAction, itemId: string) => void,
    rename: OwnCardRename, playerId?: string,
  ): void {
    const wearing = wearingLine({
      hatName: shop.items.find((item) => item.id === shop.hat)?.name,
      collarName: shop.items.find((item) => item.id === shop.collar)?.name,
    });
    this.openDialog({ kind: "own-card" }, "player-card own-card",
      openingHeading(name),
      rename.editing ? renameForm(name, rename) : renameButton(rename.start),
      portrait(picture),
      el("p", { className: "fur" }, swatchOf(shop.furColor), el("span", { text: `${shop.furColor} fur` })),
      el("p", { text: wearing ?? "Wearing nothing yet", className: "wearing" }),
      ...(hasMoreToWear(shop)
        ? []
        : [el("p", { className: "wardrobe-hint", text: "Hats, collars and coats are sold from the catalogue in the Cat Shop, off Town Square." })]),
      ...sectionsOf(wardrobeSections(shop), act),
      ...(playerId ? [details(playerId)] : []),
    );
    if (rename.editing) {
      // Not from a fresh showModal() (autofocus only applies then): moved here by hand, as the decorating panel does.
      const input = this.dialog?.element.querySelector<HTMLInputElement>("#rename-input");
      if (input && document.activeElement !== input) {
        input.focus();
        input.select();
      }
    }
  }

  /** What is open over the room: a player card (whose), the player's own card, the friends list, the catalogue, or nothing. */
  get openDialogFor(): OpenDialog | undefined {
    return this.dialog?.element.open ? this.dialog.open : undefined;
  }

  /** Closes the dialog open, if any; <byPlayer> when the player closed it (Close, Escape, a tap outside it). */
  closeDialog(byPlayer = false): void {
    const dialog = this.dialog;
    this.dialog = undefined;
    if (dialog?.element.open) {
      dialog.element.close();
    }
    dialog?.element.remove();
    if (byPlayer) {
      dialog?.closed?.();
    }
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
      button("Close", () => this.closeDialog(true), "quiet"),
    );
    dialog.setAttribute("aria-labelledby", "dialog-title");
    dialog.addEventListener("close", () => {
      if (this.dialog?.element === dialog) {
        this.closeDialog(true);
      }
    });
    // A tap on the backdrop (outside the sheet) closes it too.
    dialog.addEventListener("click", (event) => {
      if (event.target === dialog) {
        const r = dialog.getBoundingClientRect();
        if (event.clientX < r.left || event.clientX > r.right || event.clientY < r.top || event.clientY > r.bottom) {
          this.closeDialog(true);
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
    // Outside a room (showRoom names it), the game's name.
    document.title = pageTitle();
  }

  /** A brief ring where a click or tap sent the cat, in pixels from the canvas's top left. */
  markWalk(point: () => { x: number; y: number } | undefined): void {
    this.root.querySelector(".walk-marker")?.remove();
    const marker = el("div", { className: "walk-marker" });
    marker.setAttribute("aria-hidden", "true");
    this.root.append(marker);
    const follow = () => {
      const position = point();
      if (marker.isConnected && position) {
        marker.style.left = `${position.x}px`;
        marker.style.top = `${position.y}px`;
        requestAnimationFrame(follow);
      }
    };
    follow();
    window.setTimeout(() => marker.remove(), 580);
  }

  showMap(room: RoomSnapshot, travel: (id: string) => void): void {
    this.openDialog({ kind: "map" }, "map-sheet", el("h2", { text: "World map", id: "dialog-title" }), drawMap(room, travel));
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

/** How often the room is drawn, as the player set it: kept by the page, which applies it on the next frame. */
export interface FrameRateChoice {
  value: FrameRate;
  change(value: FrameRate): void;
}

function frameRateChooser(frame: FrameRateChoice): HTMLElement {
  const choice = el("select", { id: "frame-rate" });
  for (const { value, label } of FRAME_RATES) {
    const option = el("option", { text: label });
    option.value = value;
    choice.append(option);
  }
  choice.value = frame.value;
  choice.addEventListener("change", () => frame.change(choice.value as FrameRate));
  const label = el("label", { text: "Frame rate" });
  label.htmlFor = choice.id;
  return el("p", { className: "frame-rate" }, label, " ", choice);
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

/**
 * The room's buttons for the keyboard, unseen until one has focus: the player's own cat (its card), the catalogue (in
 * the shop), and a button through each door. The cat, the stand and the doors themselves are drawn on the canvas.
 */
function keyboardLinks(room: RoomSnapshot, throughDoor: (door: DoorView) => void, keys: RoomKeys): HTMLElement {
  const nav = el("nav", { className: "doors" },
    ...room.doors.map((door) => {
      const action = hasDoorway(door, room.depth, room.roomId) ? "Through the door to" : "Follow the path to";
      return button(`${action} ${door.toRoomName}`, () => throughDoor(door));
    }),
  );
  nav.setAttribute("aria-label", "Room exits");
  return el("div", { className: "keyboard-links" },
    button("Your cat", keys.yourCat),
    ...(keys.catalogue ? [button("Open the catalogue", keys.catalogue)] : []),
    nav,
    ...(room.props ?? []).flatMap((prop, index, props) => {
      const label = activityName(prop);
      if (!label) return [];
      const tree = prop.kind === "pear-tree" ? ` from tree ${props.slice(0, index + 1).filter(p => p.kind === prop.kind).length}` : "";
      return [button(label + tree, () => keys.activity(prop))];
    }),
  );
}

/** A dialog's heading that has the focus when it opens: its top, not scrolled down to the first button far below. */
function openingHeading(text: string): HTMLElement {
  const heading = el("h2", { text, id: "dialog-title" });
  heading.tabIndex = -1;
  heading.autofocus = true;
  return heading;
}

/** The parts of the catalogue or the wardrobe: each one's title, and its items. */
function sectionsOf(sections: ShopSection[], act: (action: ShopAction, itemId: string) => void): HTMLElement[] {
  return sections.flatMap((section) => [
    el("h3", { text: section.title }),
    el("ul", { className: "shop-list" }, ...section.rows.map((row) => shopItem(row, act))),
  ]);
}

/**
 * A picture of the cat on its card, described for a screen reader; shown once it is taken (a picture already taken
 * shows at once, so a card brought up to date doesn't flicker).
 */
function portrait(picture: CatPicture): HTMLElement {
  const image = el("img");
  image.alt = picture.description;
  image.width = image.height = 160;
  const { src } = picture;
  if (typeof src === "string") {
    image.src = src;
  } else {
    src?.then((url) => {
      image.src = url;
    }).catch((error: unknown) => console.warn("A cat's picture couldn't be taken", error));
  }
  return el("figure", { className: "portrait" }, image);
}

/**
 * A radio button or checkbox with its label, as one row big enough to tap: the box is drawn by the row (`.mark`), the
 * input itself spread over all of it, unseen.
 */
function choice(input: HTMLInputElement, label: string): HTMLElement {
  const mark = el("span", { className: input.type === "radio" ? "mark" : "mark check" });
  mark.setAttribute("aria-hidden", "true");
  return el("label", { className: "choice" }, input, mark, el("span", { text: label }));
}

function coinText(coins: number): string {
  return `🪙 ${coins}`;
}

function panel(...children: Node[]): HTMLElement {
  return el("div", { className: "panel" }, ...children);
}

/** A button; a "submit" one sends its form (its `onClick` isn't used: the form's submit is). */
function button(
  text: string, onClick: () => void, className = "", type: "button" | "submit" = className === "primary" && text === "Send" ? "submit" : "button",
): HTMLButtonElement {
  const element = el("button", { text, className });
  element.type = type;
  if (element.type === "button") {
    element.addEventListener("click", onClick);
  }
  return element;
}

/** A note as a paragraph (an error in red), or nothing. */
function noteParagraph(note: Note | undefined): HTMLElement[] {
  return note ? [el("p", { text: note.text, className: note.error ? "error" : "note" })] : [];
}

/** What a dialog over the room shows: another player's card, the player's own, the friends list, or the catalogue. */
export type OpenDialog =
  | { kind: "card"; playerId: string }
  | { kind: "report"; playerId: string }
  | { kind: "own-card" }
  | { kind: "friends" }
  | { kind: "map" }
  | { kind: "catalogue" }
  | { kind: "notice" };

/** The small control under the player's own name on their own card, to open the inline Rename box. */
function renameButton(start: () => void): HTMLElement {
  const edit = button("Rename", start, "quiet");
  edit.setAttribute("aria-label", "Rename your cat");
  return edit;
}

/**
 * The inline text box that replaces the Rename button once tapped, pre-filled with the cat's name now: Save sends it,
 * Cancel closes the box without saving. Big enough to tap and to type into without the page zooming in.
 */
function renameForm(name: string, rename: OwnCardRename): HTMLElement {
  const input = el("input", { id: "rename-input" });
  input.type = "text";
  input.value = name;
  // Not minLength: the browser's own validation would block the submit event (and show its own message) before the
  // form's listener ever runs. The server decides for real; a name too short comes back as a toast, same as any refusal.
  input.maxLength = maxDisplayNameLength;
  input.autocomplete = "off";
  input.setAttribute("aria-label", "New name");
  const form = el("form", { className: "rename-form" },
    input,
    button("Save", () => {}, "primary", "submit"),
    button("Cancel", rename.cancel));
  form.addEventListener("submit", (event) => {
    event.preventDefault();
    rename.save(input.value);
  });
  return form;
}

/**
 * The player's own details, folded away at the bottom of their card: their player ID, which is what makes a player an
 * admin (the server's admin list).
 */
function details(playerId: string): HTMLElement {
  const id = el("code", { text: playerId, id: "player-id" });
  return el("details", { className: "details" },
    el("summary", { text: "Details" }),
    el("p", {}, el("span", { text: "Your player ID: " }), id),
  );
}

/**
 * An admin's controls on another cat's card, folded away under "Moderate": the cat's ban and mute now, why (which the
 * player is shown), and Warn, Mute (for how long), Ban (for how long) and Lift. Another admin can't be moderated: it
 * says so instead. <before> is the section this one replaces (the same card brought up to date): open stays open, and
 * the reason typed stays.
 */
function moderateSection(
  name: string, moderation: CardModeration, moderate: (request: ModerationRequest) => void, say: (message: string) => void,
  before?: HTMLDetailsElement,
): HTMLElement {
  const section = el("details", { className: "moderate" }, el("summary", { text: "Moderate" }));
  section.open = before?.open ?? false;
  section.append(...standingLines(moderation).map((line) => el("p", { text: line, className: "standing" })));
  if (moderation.isAdmin) {
    // Not warned, muted or banned; but a ban or mute from before they were listed can still be lifted.
    section.append(el("p", { text: "This cat is an admin. Admins are changed in the server's settings, not here." }));
    if (canLift(moderation)) {
      const lift = button("Lift", () => moderate({ action: "lift", reason: "" }), "primary");
      lift.setAttribute("aria-label", `Lift ${name}`);
      section.append(el("div", { className: "moderate-row" }, lift));
    }
    return section;
  }

  const reason = el("input", { id: "moderate-reason" });
  reason.maxLength = maxReasonLength;
  reason.autocomplete = "off";
  reason.value = before?.querySelector<HTMLInputElement>("#moderate-reason")?.value ?? "";
  const label = el("label", { text: "Why (they're shown this)" });
  label.htmlFor = reason.id;

  const networks = networkChoices(moderation, before);
  const liftNetworks = el("input", { id: "lift-networks" });
  liftNetworks.type = "checkbox";
  liftNetworks.checked = before?.querySelector<HTMLInputElement>("#lift-networks")?.checked ?? true;
  const liftCat = moderation.ban !== null || moderation.mute !== null;

  const act = (action: ModerationAction, length?: string, networksOnly = false) => {
    const request = requestFor(action, reason.value, length, { networksOnly });
    if (typeof request === "string") {
      say(request);
      reason.focus();
      return;
    }
    if (action === "ban" && networks) {
      const checked = networks.boxes.filter((box) => box.checked).map((box) => box.value);
      if (request.networksOnly && checked.length === 0) {
        say("Check a network to ban first.");
        return;
      }
      if (checked.length > 0) {
        request.networks = checked;
        request.networkLength = networks.length.value;
      }
    }
    if (action === "lift") {
      request.liftCat = liftCat;
      request.liftNetworks = bannedNetworks(moderation) > 0 && (!liftCat || liftNetworks.checked);
    }
    moderate(request);
  };
  // "Mute Felix in every room": not the card's own Mute (which only hides their chat from this player).
  const labelled = (text: string, onClick: () => void, className = "") => {
    const pick = button(text, onClick, className);
    pick.setAttribute("aria-label", text === "Mute" ? `Mute ${name} in every room` : `${text} ${name}`);
    return pick;
  };
  const lengthRow = (action: "mute" | "ban", text: string, choices: readonly LengthChoice[]) => {
    const choice = el("select", { id: `${action}-length` });
    for (const { value, label: shown } of choices) {
      const option = el("option", { text: shown });
      option.value = value;
      choice.append(option);
    }
    const previous = before?.querySelector<HTMLSelectElement>(`#${action}-length`)?.value;
    if (previous) {
      choice.value = previous;
    }
    // Named in full for a screen reader; "for" is what shows, between the button and the choice.
    choice.setAttribute("aria-label", `${text} ${name} for`);
    const forLabel = el("label", { text: "for" });
    forLabel.htmlFor = choice.id;
    return el("div", { className: "moderate-row" },
      labelled(text, () => act(action, choice.value), action === "ban" ? "danger" : ""), forLabel, choice);
  };

  const renameTo = el("input", { id: "moderate-rename" });
  renameTo.maxLength = maxDisplayNameLength;
  renameTo.autocomplete = "off";
  renameTo.value = before?.querySelector<HTMLInputElement>("#moderate-rename")?.value ?? "";
  renameTo.setAttribute("aria-label", `${name}'s new name`);
  const renameLabel = el("label", { text: "New name" });
  renameLabel.htmlFor = renameTo.id;
  const renameAct = () => {
    const newName = renameTo.value.trim();
    if (newName.length === 0) {
      say("Type a new name first.");
      renameTo.focus();
      return;
    }

    moderate({ action: "rename", reason: newName });
  };

  section.append(
    el("div", { className: "moderate-reason" }, label, reason),
    el("div", { className: "moderate-row" }, labelled("Warn", () => act("warn"))),
    el("div", { className: "moderate-row moderate-rename" }, renameLabel, renameTo, labelled("Rename", renameAct)),
    lengthRow("mute", "Mute", muteLengths),
    lengthRow("ban", "Ban", banLengths),
    ...(networks ? [networks.part] : []),
    // A cat banned already: its networks can be banned alone, its own ban left as it is.
    ...(networks && moderation.ban !== null ? [networksOnlyRow(name, () => act("ban", undefined, true))] : []),
    ...(canLift(moderation) ? [el("div", { className: "moderate-row" }, labelled("Lift", () => act("lift"), "primary"))] : []),
    // Lifting the cat's ban offers to lift its networks' too (only theirs to lift: no choice).
    ...(liftCat && bannedNetworks(moderation) > 0 ? [choice(liftNetworks, "Also lift its networks' bans")] : []),
  );
  return section;
}

/** "Ban the checked networks": for a cat banned already, its networks alone (its own ban stays as it is). */
function networksOnlyRow(name: string, onClick: () => void): HTMLElement {
  const ban = button("Ban the checked networks", onClick, "danger");
  ban.setAttribute("aria-label", `Ban ${name}'s checked networks`);
  return el("div", { className: "moderate-row" }, ban);
}

/**
 * The networks the cat played from lately, on an admin's Moderate: each a box to check, with how many cats were on it this
 * week (a shared one catches others too) and its ban; the checked ones are banned with the cat, for a day or a week. None
 * seen (or network bans off): nothing.
 */
function networkChoices(
  moderation: CardModeration, before?: HTMLDetailsElement,
): { part: HTMLElement; boxes: HTMLInputElement[]; length: HTMLSelectElement } | undefined {
  const networks = moderation.networks ?? [];
  if (networks.length === 0) {
    return undefined;
  }
  const rows: HTMLElement[] = [];
  const boxes = networks.map((network) => {
    const box = el("input", { id: `network-${network.label}` });
    box.type = "checkbox";
    box.value = network.key;
    box.checked = before?.querySelector<HTMLInputElement>(`#${box.id}`)?.checked ?? false;
    rows.push(choice(box, networkLine(network)));
    return box;
  });
  const length = el("select", { id: "network-length" });
  for (const { value, label } of networkLengths) {
    const option = el("option", { text: label });
    option.value = value;
    length.append(option);
  }
  length.value = before?.querySelector<HTMLSelectElement>("#network-length")?.value ?? length.value;
  const lengthLabel = el("label", { text: "Checked ones for" });
  lengthLabel.htmlFor = length.id;
  const part = el("fieldset", { className: "networks" },
    el("legend", { text: "Its networks this week (checked: banned with it)" }),
    ...rows,
    el("div", { className: "moderate-row" }, lengthLabel, length),
  );
  return { part, boxes, length };
}


function sameDialog(a: OpenDialog, b: OpenDialog): boolean {
  if (a.kind === "card" || a.kind === "report") {
    return b.kind === a.kind && b.playerId === a.playerId;
  }
  return a.kind === b.kind;
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

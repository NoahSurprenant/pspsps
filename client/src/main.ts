import { addressNote, linkUrl, loginUrl } from "./link";
import { GameConnection, fetchMe, fetchProviders, messageOf, playAsGuest, signOut } from "./net";
import { lazily } from "./lazy";
import { PendingEvents } from "./pending";
import { comeBack } from "./rejoin";
import { catchBubble, FishingState, gotAwayMessage, paid, pondEdge, reelMessage, type Fishing } from "./fishing";
import { Room } from "./room";
import type { RoomScene } from "./scene";
import { Overlay, type Note } from "./ui";
import type { FriendAction } from "./friends";
import type { Player, PlayerCard, RoomSnapshot } from "./types";

const overlay = new Overlay(required<HTMLElement>("#ui"));
overlay.onFriends = () => void openFriends();
// The 3D scene, made on the way into the first room: Babylon (most of the page's script) loads only then, so the
// sign-in screen and the world list stay light, and nothing draws until there is a room to draw.
let scene: RoomScene | undefined;

// A scene that failed to load (a dropped connection, a deploy that replaced the chunks) is tried again on the next room.
const stage = lazily(async (): Promise<RoomScene> => {
  const [{ Engine }, { RoomScene }] = await Promise.all([import("@babylonjs/core/Engines/engine"), import("./scene")]);
  const canvas = required<HTMLCanvasElement>("#game");
  // Babylon makes the canvas a Tab stop (tabindex 1), again whenever the pointer moves over it; the room is played
  // with the pointer and the buttons, so it isn't a stop at all.
  const engine = new Engine(canvas, true, { stencil: true, canvasTabIndex: -1 });
  let made: RoomScene;
  try {
    made = new RoomScene(engine);
  } catch (error) {
    // Nothing left behind for the next try: it makes its own engine.
    engine.dispose();
    throw error;
  }
  window.addEventListener("resize", () => engine.resize());
  made.onFloorClicked((x, z) => {
    if (room) {
      // Walking off reels the line in (the server drops it), with nothing on it.
      fishing.drop();
      connection.move(x, z).catch((error: unknown) => overlay.toast(messageOf(error)));
    }
  });
  made.onPondClicked(() => void fish());
  // Another cat: its player card. The player's own cat has none.
  made.onCatClicked((playerId) => {
    if (room && playerId !== me?.id) {
      void openCard(playerId);
    }
  });
  scene = made;
  return made;
});

let room: Room | undefined;
// Who is playing, once known: the world picker needs it again when a world is lost. Their coins (me.coins) are the
// ones shown in the room bar, kept up to date by every catch.
let me: Player | undefined;
// Where the player's fishing line is.
const fishing = new FishingState((state) => overlay.setFishing(state));
// Room events that arrive while the next room loads, for it once its snapshot shows.
const loading = new PendingEvents<(room: Room) => void>();

// Read-only: the end-to-end tests (e2e/) read the room here, since the cats and chat are drawn on a canvas.
declare global {
  interface Window {
    pspsps?: {
      room(): Room | undefined;
      coins(): number;
      fishing(): Fishing;
      /** Where a cat in the room is on the page, to click it. */
      catOnScreen(playerId: string): { x: number; y: number } | undefined;
    };
  }
}
window.pspsps = {
  room: () => room,
  coins: () => me?.coins ?? 0,
  fishing: () => fishing.state,
  catOnScreen: (playerId) => scene?.screenPositionOf(playerId),
};

const connection = new GameConnection({
  catJoined: (cat) => onRoom((here) => {
    here.join(cat);
    scene?.place(cat);
  }),
  catLeft: (playerId) => onRoom((here) => {
    here.leave(playerId);
    scene?.remove(playerId);
  }),
  catMoved: (playerId, x, z) => onRoom((here) => {
    if (here.move(playerId, x, z)) {
      scene?.walk(playerId, x, z);
    }
  }),
  chatSaid: (line) => onRoom((here) => {
    if (here.say(line)) {
      scene?.speak(line.playerId, line.text);
    }
  }),
  catEmoted: (emote) => onRoom((here) => {
    here.emote(emote);
    scene?.emote(emote.playerId, emote.emote);
  }),
  fishBit: () => fishing.bite(),
  fishGotAway: () => {
    if (fishing.gotAway()) {
      overlay.toast(gotAwayMessage);
    }
  },
  catFished: (caught) => onRoom(() => scene?.speak(caught.playerId, catchBubble(caught.fishName))),
  replaced: () => {
    loading.cancel();
    room = undefined;
    overlay.showReplaced(() => window.location.reload());
  },
  // The server dropped us from the room with the old connection: walk back in.
  reconnected: () => rejoin("reconnected"),
  // The room no longer has our cat; we are still in the world: go back to the room.
  roomLost: () => rejoin("roomLost"),
  // The world has no room for us any more: pick another.
  worldLost: (message) => {
    loading.cancel();
    room = undefined;
    overlay.toast(message);
    if (me) {
      void pickWorld(me);
    }
  },
  friendsChanged: (playerId) => void friendsChanged(playerId),
});

/** Applies a room event to the room showing, or holds it for the one loading. */
function onRoom(apply: (room: Room) => void): void {
  if (!loading.hold(apply) && room) {
    apply(room);
  }
}

/** Walks the player's cat to the pond's edge and casts: once, however many clicks. */
async function fish(): Promise<void> {
  const pond = room?.snapshot.pond;
  const cat = me && room?.cats.get(me.id);
  if (!pond || !cat) {
    return;
  }
  const cast = fishing.startCast();
  if (cast === undefined) {
    return;
  }
  try {
    const spot = pondEdge(pond, cat);
    await connection.move(spot.x, spot.z);
    // Not if a floor click meanwhile walked the cat off (and dropped this cast).
    if (!fishing.isCasting(cast)) {
      return;
    }
    await connection.cast();
    // Waiting only if still casting: a floor click meanwhile walked off, and the server dropped the line.
    fishing.castDone(cast);
  } catch (error) {
    fishing.castFailed(cast);
    overlay.toast(messageOf(error));
  }
}

/** Reels the line in, once however many clicks and keys: the catch (or why not) as a toast, and the coins it paid. */
async function reel(): Promise<void> {
  if (!fishing.startReel()) {
    return;
  }
  try {
    const result = await connection.reel();
    overlay.toast(reelMessage(result));
    if (me) {
      // Kept on the player: a world picked again (after worldLost) shows these coins, not the page load's.
      me = paid(me, result);
      overlay.setCoins(me.coins);
    }
  } catch (error) {
    overlay.toast(messageOf(error));
  } finally {
    fishing.reeled();
  }
}

function show(snapshot: RoomSnapshot, view: RoomScene): Room {
  const shown = new Room(snapshot);
  room = shown;
  view.show(snapshot);
  overlay.showRoom(
    snapshot,
    (roomId) => pick(() => goTo(roomId)),
    (text) => connection.say(text).catch((error: unknown) => overlay.toast(messageOf(error))),
    // Between rooms (room undefined while the next one loads) an emote would only be refused.
    (emote) => {
      if (room) {
        connection.emote(emote).catch((error: unknown) => overlay.toast(messageOf(error)));
      }
    },
    () => window.location.reload(),
    me?.coins ?? 0,
    snapshot.pond ? { fish: () => void fish(), reel: () => void reel(), state: () => fishing.state } : undefined,
  );
  fishing.drop();
  return shown;
}

let picking = false;

/**
 * Goes where the player picked, one pick at a time: taps while one loads (the first room downloads the scene) are
 * ignored, rather than loading two rooms over each other.
 */
function pick(go: () => Promise<void>): void {
  if (picking) {
    return;
  }
  picking = true;
  void go().finally(() => {
    picking = false;
  });
}

function rejoin(reason: "roomLost" | "reconnected"): void {
  const current = room?.snapshot;
  if (current) {
    void comeBack(
      reason,
      current,
      async (worldId) => {
        await enter(worldId);
        return room?.snapshot.roomId;
      },
      goTo,
    );
  }
}

async function enter(worldId: string): Promise<void> {
  await load(() => connection.enterWorld(worldId));
}

async function goTo(roomId: string): Promise<void> {
  await load(() => connection.goToRoom(roomId));
}

/**
 * Leaves the room showing and loads the next. The server sends the new room's events (our own join among them) from
 * just before it takes the snapshot, so they are held until the snapshot shows and then replayed onto it, in order;
 * the room's updates are idempotent, so those already in the snapshot change nothing.
 */
async function load(request: () => Promise<RoomSnapshot>): Promise<void> {
  room = undefined;
  loading.start();
  let shown: Room;
  try {
    // The 3D scene before the room (the first room loads it): had it failed after, the server would have the cat in a
    // room the page can't show.
    const view = await stage();
    shown = show(await request(), view);
  } catch (error) {
    loading.cancel();
    overlay.toast(messageOf(error));
    return;
  }
  for (const apply of loading.finish()) {
    apply(shown);
  }
}

/** The world list; `providers` when they're already known (a guest who just signed in from the sign-in panel). */
async function pickWorld(player: Player, providers?: string[], note?: Note): Promise<void> {
  me = player;
  await connection.start();
  // A guest can save their cat with any provider the server offers.
  const [saveWith, worlds] = await Promise.all([
    providers ?? (player.isGuest ? fetchProviders() : []),
    connection.getWorlds(),
  ]);
  overlay.showWorlds(
    player,
    worlds,
    (worldId) => pick(() => enter(worldId)),
    () => void signOut().then(() => window.location.reload()),
    { providers: saveWith, link: (provider) => postTo(linkUrl(provider)) },
    note,
  );
}

async function start(): Promise<void> {
  const search = window.location.search;
  const player = await fetchMe();
  // How signing in or saving the cat went, said once: a reload doesn't say it again.
  const note = addressNote(search, player !== null);
  if (note) {
    window.history.replaceState(null, "", window.location.pathname);
  }

  if (player) {
    await pickWorld(player, undefined, note);
    return;
  }

  const providers = await fetchProviders();
  overlay.showSignIn(
    providers,
    (provider) => window.location.assign(loginUrl(provider)),
    () => void playAsGuest().then((guest) => pickWorld(guest, providers)).catch((error: unknown) => overlay.toast(messageOf(error))),
    note,
  );
}

/** Shows another cat's player card, as the server has it now. */
async function openCard(playerId: string): Promise<void> {
  try {
    showCard(await connection.getPlayerCard(playerId));
  } catch (error) {
    overlay.toast(messageOf(error));
  }
}

function showCard(card: PlayerCard): void {
  overlay.showPlayerCard(card, (action) => void changeFriendship(action, card.playerId));
}

/** The friends list, with where each friend is now. */
async function openFriends(): Promise<void> {
  try {
    const list = await connection.getFriends();
    const here = room ? { worldId: room.snapshot.worldId, roomId: room.snapshot.roomId } : undefined;
    overlay.showFriends(list, here, {
      card: (playerId) => void openCard(playerId),
      goTo: (playerId) => {
        overlay.closeDialog();
        pick(() => load(() => connection.goToFriend(playerId)));
      },
      act: (action, playerId) => void changeFriendship(action, playerId),
    });
  } catch (error) {
    overlay.toast(messageOf(error));
  }
}

/** Adds, answers, takes back or ends a friendship, then shows the card or list that is open as it is after. */
async function changeFriendship(action: FriendAction, playerId: string): Promise<void> {
  try {
    const card = await connection.changeFriendship(action, playerId);
    const open = overlay.openDialogFor;
    if (open?.kind === "card" && open.playerId === playerId) {
      showCard(card);
    } else if (open?.kind === "friends") {
      await openFriends();
    }
  } catch (error) {
    overlay.toast(messageOf(error));
  }
}

/**
 * Another player changed their friendship with this one: the card or list showing it is brought up to date, and a new
 * request is said.
 */
async function friendsChanged(playerId: string): Promise<void> {
  const open = overlay.openDialogFor;
  if (open?.kind === "friends") {
    await openFriends();
    return;
  }
  try {
    const card = await connection.getPlayerCard(playerId);
    const now = overlay.openDialogFor;
    if (now?.kind === "card" && now.playerId === playerId) {
      showCard(card);
    } else if (card.friendState === "RequestReceived") {
      overlay.toast(`${card.displayName} wants to be friends! Open Friends to answer.`);
    }
  } catch {
    // Only a notice: the list shows it when opened.
  }
}

/** Leaves the page with a form post to `url` (the browser marks it as from this site), as a submit button would. */
function postTo(url: string): void {
  const form = document.createElement("form");
  form.method = "post";
  form.action = url;
  document.body.append(form);
  form.submit();
}

function required<T extends Element>(selector: string): T {
  const element = document.querySelector<T>(selector);
  if (!element) {
    throw new Error(`index.html has no ${selector}`);
  }
  return element;
}

void start();

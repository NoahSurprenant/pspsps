import { appLog, startTelemetry, telemetry } from "./telemetry";
import { FpsSampler, watchBabylonLog, watchWebGlContext } from "./telemetry/performance";
import { DebugOverlay } from "./debugOverlay";
import { DoorWalks, doorAt } from "./doors";
import { addressNote, linkUrl, loginUrl } from "./link";
import {
  GameConnection, NetworkBannedError, fetchMe, fetchNetworkBan, fetchProviders, messageOf, playAsGuest, signOut,
} from "./net";
import { lazily } from "./lazy";
import { loadRoom, PendingEvents } from "./pending";
import { comeBack, saysSentOutAgain, stayAfterRefusal } from "./rejoin";
import { catchBubble, FishingState, gotAwayMessage, inReachOfPond, paid, pondEdge, reelMessage, type Fishing } from "./fishing";
import { playPiano } from "./roomActivities";
import { Room } from "./room";
import { roomGestures } from "./roomGestures";
import type { CatAnimation } from "./cat";
import type { RoomScene } from "./scene";
import { Overlay, type CatPicture, type DecorateActions, type Note } from "./ui";
import { catalogueStand, goRead } from "./catalogue";
import type { Outfit } from "./cat";
import {
  canVisitFromCard, decoratingHint, furnitureLeft, isOwnHome, pieceAt, propOf, propsOf, visitorsMessage, type Decorating,
} from "./home";
import type { FriendAction } from "./friends";
import type { OwnCardRename } from "./rename";
import { describeCat, shopMessage, showsOutfit, type ShopAction } from "./shop";
import { Notices, doneMessage, inForce, mutedToast, type ModerationRequest, type Sanction } from "./moderation";
import { Mutes, muteMessage, reportRequest, reportSentMessage, type ReportDraft, type SafetyAction } from "./safety";
import { pageTitle } from "./title";
import { WalkKeys } from "./walkKeys";
import { FRAME_RATE_KEY, maxFps, savedFrameRate, type FrameRate } from "./frameLimit";
import { stepInRoom } from "./walking";
import { nearestPond, place, stepInWorld, worldDoors } from "./overworld";
import type {
  DoorView, HomeView, HomeVisitors, MutedView, OutfitView, Player, PlayerCard, PropView, RoomSnapshot, Shop,
} from "./types";

// First, so the errors of everything after are reported (when telemetry is on: docs/telemetry.md).
startTelemetry();

const overlay = new Overlay(required<HTMLElement>("#ui"));
overlay.onFriends = () => void openFriends();
// The 3D scene, made on the way into the first room: Babylon (most of the page's script) loads only then, so the
// sign-in screen and the world list stay light, and nothing draws until there is a room to draw.
let scene: RoomScene | undefined;
let frameRate: FrameRate = readFrameRate();

// A scene that failed to load (a dropped connection, a deploy that replaced the chunks) is tried again on the next room.
const stage = lazily(async (): Promise<RoomScene> => {
  try {
    return await makeStage();
  } catch (error) {
    appLog.error("The 3D scene didn't load", error);
    throw error;
  }
});

async function makeStage(): Promise<RoomScene> {
  const [{ Engine }, { RoomScene }, { Logger }, { loadPropDesigns }, { loadLooks }] = await Promise.all([
    import("@babylonjs/core/Engines/engine"), import("./scene"), import("@babylonjs/core/Misc/logger"), import("./props"), import("./looks"),
  ]);
  await Promise.all([loadPropDesigns(), loadLooks()]);
  const canvas = required<HTMLCanvasElement>("#game");
  if (telemetry.enabled) {
    watchBabylonLog(Logger, appLog);
    watchWebGlContext(canvas, appLog);
  }
  // Babylon makes the canvas a Tab stop (tabindex 1), again whenever the pointer moves over it; the room is played
  // with the pointer and the buttons, so it isn't a stop at all.
  const engine = new Engine(canvas, true, { stencil: true, canvasTabIndex: -1 });
  let made: RoomScene;
  try {
    const touch = matchMedia("(pointer: coarse)").matches;
    made = new RoomScene(engine, undefined, touch, () => maxFps(frameRate, touch));
  } catch (error) {
    // Nothing left behind for the next try: it makes its own engine.
    engine.dispose();
    throw error;
  }
  window.addEventListener("resize", () => engine.resize());
  roomGestures(canvas, (x, y) => made.tapScreen(x, y), (factor) => made.zoomBy(factor));
  if (telemetry.enabled) {
    // The frame rate while a room shows, a median every few seconds.
    const fps = new FpsSampler((value) => telemetry.vital("FPS", value), () => room !== undefined);
    engine.onEndFrameObservable.add(() => fps.frame(performance.now()));
  }
  engine.onEndFrameObservable.add(() => {
    if (debug.shown) {
      debug.stats.frame(performance.now());
    }
  });
  made.onFloorClicked((x, z) => {
    if (!room) {
      return;
    }
    markWalk(x, z);
    // Walking onto a door goes through it.
    const door = doorAt(worldDoors(room.snapshot), x, z);
    if (door) {
      void goThrough(door);
      return;
    }
    void moveCat(x, z);
  });
  made.onPondClicked(() => void fish());
  made.onDoorClicked((door) => {
    markWalk(door.x, door.z);
    void goThrough(door);
  });
  made.onEditTapped((x, z) => editTap(x, z));
  // A cat: its player card; the player's own, their own card (what they wear, to change it).
  made.onCatClicked((playerId) => {
    if (!room) {
      return;
    }
    if (playerId === me?.id) {
      void openOwnCard();
    } else {
      void openCard(playerId);
    }
  });
  // The shop's catalogue stand: the cat walks up to it, and the catalogue opens.
  made.onStandClicked(() => void readCatalogue());
  scene = made;
  return made;
}

let room: Room | undefined;
// The room the page showed last, for walking back in after a room lost while it shows none.
let lastShown: RoomSnapshot | undefined;
// Decorating the player's own home: what is picked or selected, and the furniture they have (the shop, as last read).
let decorating: Decorating | undefined;
let decoratingShop: Shop | undefined;
// Who is playing, once known: the world picker needs it again when a world is lost. Their coins (me.coins) are the
// ones shown in the room bar, kept up to date by every catch.
let me: Player | undefined;
// Whether the own card's inline Rename box is open now, in place of the small Rename button.
let renamingOwnCard = false;
// Where the player's fishing line is.
const fishing = new FishingState((state) => overlay.setFishing(state));
// The walk to a door (which goes through it once the cat gets there) or to the catalogue stand (which opens it).
const doorWalks = new DoorWalks();
const walkKeys = new WalkKeys(
  () => !!room && !decorating && !overlay.openDialogFor && !picking && loads === 0,
  walkByKey,
);
// Room events that arrive while the next room loads, for it once its snapshot shows.
const loading = new PendingEvents<(room: Room) => void>();
// Whom the player has muted: their chat lines and bubbles never show here.
const mutes = new Mutes();

// Read-only: the end-to-end tests (e2e/) read the room here, since the cats and chat are drawn on a canvas.
declare global {
  interface Window {
    pspsps?: {
      room(): Room | undefined;
      coins(): number;
      fishing(): Fishing;
      /** The animation a cat in the room is playing ("idle", "walk", ...), once the cat is drawn. */
      animation(playerId: string): CatAnimation | undefined;
      /** Where a spot on the floor is drawn on the canvas, in CSS pixels, to click it; undefined with no room showing. */
      screenPoint(x: number, z: number): { x: number; y: number } | undefined;
      /** Where a cat in the room is drawn on the floor now, as it walks (not where it is walking to). */
      catDrawnAt(playerId: string): { x: number; z: number } | undefined;
      /** Where a cat in the room is on the page, to click it. */
      catOnScreen(playerId: string): { x: number; y: number } | undefined;
      /** What a cat in the room is drawn wearing (its fur, hat and collar), once the cat is drawn. */
      wearing(playerId: string): { fur: string; hat: string | undefined; collar: string | undefined } | undefined;
      /** The home the room is, as it is now (its furniture and who may visit); undefined in a world's room. */
      home(): HomeView | undefined;
      /** The props the room is drawn with now: a home's furniture, once drawn. */
      drawnProps(): readonly PropView[];
      /** What the owner is doing while decorating their home; undefined when not decorating. */
      decorating(): Decorating | undefined;
      /** The IDs of the players this one has muted, as this tab knows them. */
      mutes(): string[];
      /** What the bubble over a cat in the room says now, if anything. */
      bubble(playerId: string): string | undefined;
      /** A picture of a cat dressed in <outfit>, as the player cards show it, once a room has loaded the model. */
      portrait(outfit: Outfit): Promise<string> | undefined;
      /** The hat and collar IDs the shop catalogue draws (wearables.ts), for the wardrobe's contact sheet. */
      wardrobe(): Promise<{ hats: readonly string[]; collars: readonly string[] }>;
    };
  }
}
window.pspsps = {
  room: () => room,
  coins: () => me?.coins ?? 0,
  fishing: () => fishing.state,
  animation: (playerId) => scene?.animationOf(playerId),
  screenPoint: (x, z) => scene?.screenPoint(x, z),
  catDrawnAt: (playerId) => scene?.positionOf(playerId),
  catOnScreen: (playerId) => scene?.screenPositionOf(playerId),
  wearing: (playerId) => scene?.wearingOf(playerId),
  home: () => room?.snapshot.home ?? undefined,
  drawnProps: () => scene?.drawnProps ?? [],
  decorating: () => decorating,
  mutes: () => mutes.list.map((muted) => muted.playerId),
  bubble: (playerId) => scene?.bubbleOf(playerId),
  portrait: (outfit) => scene?.portraits.picture(outfit),
  // A dynamic import, not a top-level one: wearables.ts pulls in Babylon's mesh builders, which the sign-in screen must
  // not load before there is a room to draw.
  wardrobe: async () => {
    const { hatIds, collarIds } = await import("./wearables");
    return { hats: hatIds, collars: collarIds };
  },
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
  ballMoved: (ball) => onRoom(() => scene?.moveBall(ball)),
  catMoved: (playerId, x, z) => onRoom((here) => {
    if (here.move(playerId, x, z)) {
      scene?.walk(playerId, x, z);
    }
    if (playerId === me?.id) {
      showFishButton();
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
  catDressed: (outfit) => {
    onRoom((here) => {
      if (here.dress(outfit)) {
        scene?.dress(outfit);
      }
    });
    void dressed(outfit);
  },
  catRenamed: (rename) => {
    onRoom((here) => {
      if (here.rename(rename)) {
        scene?.rename(rename.playerId, rename.displayName);
      }
    });
    void refreshCard(rename.playerId);
  },
  // This room is a home whose owner (not necessarily our cat) was renamed: shown as their new name's home.
  homeRenamed: (ownerName) => onRoom((here) => {
    if (here.renameHome(ownerName) && here === room) {
      document.title = pageTitle(here.snapshot.roomName);
    }
  }),
  // We renamed our own cat, from this tab or another: the own card and catalogue, if open, show it; CatRenamed (above)
  // brings the room's label (and any open card of ours) up to date on its own.
  renamed: (displayName) => {
    if (me) {
      me = { ...me, displayName };
    }
    renamingOwnCard = false;
    const open = overlay.openDialogFor;
    if (open?.kind === "own-card" || open?.kind === "catalogue") {
      void openShopSheet(open.kind, true);
    }
  },
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
  reconnected: () => {
    debug.stats.note("connection back", performance.now());
    // Muted or unmuted elsewhere while the connection was down, maybe.
    void loadMutes();
    // And by an admin: a mute or lift, or a warning, sent while the connection was down never came.
    void refreshStanding();
    rejoin("reconnected");
  },
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
  homeChanged: (home) => onRoom((here) => applyHome(home, here)),
  // The home's owner stopped letting us in: back to Town Square, in the world we came from.
  sentOut: (ownerId, message) => {
    // Banned: the server takes the cat out of its room too; nothing to walk back to.
    if (isBanned) {
      return;
    }
    overlay.toast(message);
    // Gone after whatever is loading now (the home itself, just come into): the tap that loads it would drop this.
    sentOutOfHome = { ownerId, message };
    leaveHomeIfSentOut();
  },
  mutesChanged: (list) => applyMutes(list),
  // An admin banned the player: this tab has stopped; it says why and until when, and nothing more.
  banned: (ban) => {
    isBanned = true;
    loading.cancel();
    room = undefined;
    overlay.showBanned(ban);
  },
  muted: (mute) => {
    setChatMute(mute);
    if (mute) {
      overlay.toast(mutedToast(mute));
    }
  },
  warned: () => void showWarning(),
  // The connection dropped, or couldn't come back: a ban (refused at the door) shows why; anything else, as before.
  reconnecting: () => {
    debug.stats.note("connection dropped, reconnecting", performance.now());
    void showBanIfBanned();
  },
  closed: () => {
    debug.stats.note("connection closed", performance.now());
    void showBanIfBanned();
  },
});

// F3 (or ?debug) shows the room, frame rate and connection health in a corner.
const debug = new DebugOverlay(
  () => {
    if (!room) {
      return undefined;
    }
    const cat = me && room.cats.get(me.id);
    return {
      roomName: room.snapshot.roomName, roomId: room.snapshot.roomId, spot: cat, cats: room.cats.size, connection: connection.state,
    };
  },
  () => connection.ping(),
  localStorage,
  window.location.search,
  debugBox(),
  window,
);

// Set once the player is banned in this tab: it has stopped, and does nothing more.
let isBanned = false;

/** The player's mute and warnings as the server has them now (after a dropped connection, which missed any news). */
async function refreshStanding(): Promise<void> {
  try {
    const player = await fetchMe();
    if (player?.ban) {
      await showBanIfBanned();
      return;
    }
    setChatMute(player?.mute ?? null);
  } catch {
    // Not answered: what the page knew stays.
  }
  await showWarning();
}

/** Asks the server whether the player is banned (a dropped connection may be why), and if so stops and shows why. */
async function showBanIfBanned(): Promise<void> {
  try {
    const player = await fetchMe();
    if (player?.ban && !isBanned) {
      isBanned = true;
      loading.cancel();
      room = undefined;
      connection.stop();
      overlay.showBanned(player.ban);
    }
  } catch {
    // Not answered: the connection's own retries go on.
  }
}

// The mute the player is under (why, until when), if any: the chat box is off until it ends, by itself or lifted.
let chatMute: Sanction | null = null;
let unmuteTimer: number | undefined;

function setChatMute(mute: Sanction | null): void {
  window.clearTimeout(unmuteTimer);
  unmuteTimer = undefined;
  chatMute = inForce(mute) ? mute : null;
  // Kept on the player too: a world picked again (after worldLost) starts from it, not from the page load's.
  if (me) {
    me = { ...me, mute: chatMute };
  }
  overlay.setChatMute(chatMute);
  if (chatMute?.until) {
    // A timeout of more than about 24.8 days fires at once: a long mute is checked again in a day.
    const left = Math.min(new Date(chatMute.until).getTime() - Date.now(), 24 * 60 * 60 * 1000);
    unmuteTimer = window.setTimeout(() => setChatMute(chatMute), left);
  }
}

// Warnings from the admins, each shown until the player closes it; only then is the server told it was seen.
const notices = new Notices();

/** The warnings admins left the player that they haven't seen, shown over whatever shows until closed. */
async function showWarning(): Promise<void> {
  try {
    for (const warning of await connection.getWarnings()) {
      notices.show(warning);
    }
    showNotice();
  } catch {
    // Not read: they are still there, for the next time.
  }
}

/** Shows the warning waiting, unless it shows already: again after a change of view (a room loading) closed it. */
function showNotice(): void {
  const warning = notices.current;
  if (warning !== undefined && overlay.openDialogFor?.kind !== "notice") {
    overlay.showNotice("A word from the admins", warning.text, () => {
      const seen = notices.closed();
      if (seen) {
        // Not told (the connection dropped): it shows again next time, which is better than never.
        connection.sawWarning(seen.id).catch(() => {});
      }
      showNotice();
    });
  }
}

/** The element the debug overlay writes in: hidden, out of the way of the page's own controls and of screen readers. */
function debugBox(): HTMLPreElement {
  const box = document.createElement("pre");
  box.id = "debug-overlay";
  box.setAttribute("aria-hidden", "true");
  document.body.append(box);
  return box;
}

/** Applies a room event to the room showing, or holds it for the one loading. */
function onRoom(apply: (room: Room) => void): void {
  if (!loading.hold(apply) && room) {
    apply(room);
  }
}

/** Shows the Fish button only while the player's cat stands within reach of a pond. */
function showFishButton(): void {
  const cat = me && room?.cats.get(me.id);
  const chunks = room?.snapshot.chunks;
  const ponds = chunks?.length ? chunks.map(place).flatMap((chunk) => (chunk.pond ? [chunk.pond] : [])) : [];
  if (!chunks?.length && room?.snapshot.pond) {
    ponds.push(room.snapshot.pond);
  }
  overlay.setPondNear(cat ? inReachOfPond(ponds, cat) : false);
}

/** Walks the player's cat to the pond's edge and casts: once, however many clicks. */
async function fish(): Promise<void> {
  const cat = me && room?.cats.get(me.id);
  const chunks = room?.snapshot.chunks;
  const pond = chunks && cat ? nearestPond(chunks.map(place), cat) : room?.snapshot.pond;
  if (!pond || !cat) {
    return;
  }
  const cast = fishing.startCast();
  if (cast === undefined) {
    return;
  }
  doorWalks.callOff();
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

function markWalk(x: number, z: number): void {
  overlay.markWalk(() => scene?.screenPoint(x, z));
}

async function moveCat(x: number, z: number): Promise<void> {
  doorWalks.callOff();
  // Walking off reels the line in (the server drops it), with nothing on it.
  if (fishing.state !== "idle") {
    fishing.drop();
  }
  try {
    await connection.move(x, z);
  } catch (error) {
    walkKeys.stop();
    overlay.toast(messageOf(error));
  }
}

async function walkByKey(dx: number, dz: number): Promise<void> {
  const here = room;
  const cat = me && here?.cats.get(me.id);
  if (!here || !cat) {
    return;
  }
  const position = scene?.positionOf(cat.playerId) ?? cat;
  const target = here.snapshot.chunks?.length
    ? stepInWorld(here.snapshot.chunks, position, dx, dz)
    : stepInRoom(position, dx, dz, here.snapshot);
  if (target.x === cat.x && target.z === cat.z) {
    return;
  }
  const door = doorAt(worldDoors(here.snapshot), target.x, target.z);
  if (door) {
    await goThrough(door);
  } else {
    await moveCat(target.x, target.z);
  }
}

/**
 * Walks the player's cat onto a door and, once it is drawn standing there, through it: the server checks the cat is on
 * it. Called off by any other walk meanwhile.
 */
async function goThrough(door: DoorView): Promise<void> {
  const view = scene;
  if (!room || !me || !view) {
    return;
  }
  walkKeys.stop();
  const walk = doorWalks.start();
  // Walking off reels the line in, as any walk does.
  fishing.drop();
  try {
    await connection.move(door.x, door.z);
    const there = await view.reached(me.id, door.x, door.z);
    if (there && doorWalks.isGoing(walk)) {
      doorWalks.callOff();
      // A door that refuses the cat (or a full room past it, which sends it back in) keeps it in this room.
      const here = room;
      pick(() => load(() => connection.goThroughDoor(door.id), here));
    }
  } catch (error) {
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
  const shown = new Room(snapshot, (playerId) => mutes.has(playerId));
  room = shown;
  lastShown = snapshot;
  // Decorating is for the home it started in.
  decorating = undefined;
  decoratingShop = undefined;
  const home = snapshot.home;
  view.playerId = me?.id;
  view.show(snapshot);
  view.onRoomEntered((chunk) => {
    if (room === shown) {
      shown.enter(chunk.roomId, chunk.name);
      document.title = pageTitle(chunk.name);
    }
  });
  const travel = (roomId: string) => {
    if (room !== shown || picking) return;
    doorWalks.callOff();
    fishing.drop();
    overlay.closeDialog();
    pick(() => goTo(roomId, shown));
  };
  const activity = async (prop: PropView) => {
    if (room !== shown || picking || !me) return;
    walkKeys.stop();
    doorWalks.callOff();
    if (prop.kind === "map-board") return overlay.showMap(shown.snapshot, travel);
    if (prop.kind === "mirror") return openOwnCard();
    if (prop.kind === "rod-rack") return fish();
    if (prop.kind === "piano") {
      overlay.toast("♪ A little café waltz ♪");
      await playPiano();
      return;
    }
    const spot = view.approach(prop, snapshot);
    if (!spot) return;
    fishing.drop();
    const walk = doorWalks.start();
    const id = me.id;
    await connection.move(spot.x, spot.z);
    if (await view.reached(id, spot.x, spot.z) && room === shown && doorWalks.isGoing(walk)) {
      doorWalks.callOff();
      overlay.toast(view.tend(prop));
    }
  };
  const useActivity = (prop: PropView) => void activity(prop).catch((error: unknown) => overlay.toast(messageOf(error)));
  view.onActivityClicked(useActivity);
  overlay.showRoom(
    snapshot,
    (text) => connection.say(text).catch((error: unknown) => overlay.toast(messageOf(error))),
    // Between rooms (room undefined while the next one loads) an emote would only be refused.
    (emote) => {
      if (room) {
        connection.emote(emote).catch((error: unknown) => overlay.toast(messageOf(error)));
      }
    },
    (door) => void goThrough(door),
    () => window.location.reload(),
    me?.coins ?? 0,
    {
      yourCat: () => void openOwnCard(),
      catalogue: catalogueStand(snapshot.props) ? () => void readCatalogue() : undefined,
      activity: useActivity,
      travel,
    },
    snapshot.pond || snapshot.chunks?.some((chunk) => chunk.pond) ? { fish: () => void fish(), reel: () => void reel(), state: () => fishing.state } : undefined,
    // In their own home, the owner decorates it and chooses who may visit.
    home && isOwnHome(home, me?.id)
      ? { decorate: () => void startDecorating(), visitors: home.visitors, letIn: (visitors) => void letIn(visitors) }
      : undefined,
  );
  overlay.setChatMute(chatMute);
  showNotice();
  showFishButton();
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
    leaveHomeIfSentOut();
  });
}

// The home the cat was last sent out of, and why, until a room shows with nothing loading.
let sentOutOfHome: { ownerId: string; message: string } | undefined;

/**
 * Back to Town Square, in the world the cat is in, once nothing is loading, if the home showing is the one that sent the
 * cat out. Any other room (the player's own home, say) stays.
 */
function leaveHomeIfSentOut(): void {
  const out = sentOutOfHome;
  if (out === undefined || picking || loads > 0) {
    return;
  }
  // Looked at once, when whatever was loading has finished, room or not: never later, in a home that lets the cat in.
  sentOutOfHome = undefined;
  if (room?.snapshot.home?.ownerId === out.ownerId) {
    pick(async () => {
      await goTo("town-square");
      // Said again once out in Town Square; a cat that landed elsewhere was told where and why, and a walk that failed
      // has said why instead.
      if (saysSentOutAgain(room?.snapshot)) {
        overlay.toast(out.message);
      }
    });
  }
}

function rejoin(reason: "roomLost" | "reconnected"): void {
  // A room lost while the page shows none (a way back that failed after a load cleared it): the one it showed last.
  const current = room?.snapshot ?? (reason === "roomLost" && !picking ? lastShown : undefined);
  if (current) {
    void comeBack(
      reason,
      current,
      async (worldId) => {
        await enter(worldId);
        return room?.snapshot.roomId;
      },
      // Back into the home the cat was in, if it still lets the cat in; Town Square if not.
      current.home ? () => backInto(current.home?.ownerId ?? "") : (roomId) => goTo(roomId, stayAfterRefusal(reason, room)),
    );
  }
}

/** Goes into a player's home, if it lets this player in: the cat stays where it is otherwise, and is told why. */
function visitHome(ownerId: string): void {
  const stay = room;
  pick(() => load(() => connection.visitHome(ownerId), stay));
}

/** Back into a home after the server forgot the cat there; to Town Square when the home no longer lets it in. */
async function backInto(ownerId: string): Promise<void> {
  // After a reconnect the cat is in the world's Town Square already: refused, it stays there. After a lost room the page
  // still shows the home, which the server no longer has the cat in: refused, it goes to Town Square.
  const stay = room?.snapshot.home?.ownerId === ownerId ? undefined : room;
  await load(() => connection.visitHome(ownerId), stay);
  if (!room) {
    await goTo("town-square");
  }
}

async function enter(worldId: string): Promise<void> {
  await load(() => connection.enterWorld(worldId));
}

/**
 * Goes to a room of the world; refused (the server lands the cat in the next place on its way out, which shows instead,
 * unless nothing answered), the page shows <stay>, where the server still has the cat, or no room.
 */
async function goTo(roomId: string, stay?: Room): Promise<void> {
  await load(() => connection.goToRoom(roomId), stay);
}

/**
 * Leaves the room showing and loads the next. The server sends the new room's events (our own join among them) from
 * just before it takes the snapshot, so they are held until the snapshot shows and then replayed onto it, in order;
 * the room's updates are idempotent, so those already in the snapshot change nothing. A room that refused the cat can
 * send its own events in that same window (chat, another cat's moves): the snapshot's notice says the cat landed
 * somewhere else, so those are dropped rather than replayed onto the room that shows.
 * A load that fails leaves the page in `stay` (with what it said meanwhile) when the server kept the cat there.
 */
async function load(request: () => Promise<RoomSnapshot>, stay?: Room): Promise<void> {
  walkKeys.stop();
  room = undefined;
  loads++;
  try {
    // The 3D scene before the room (the first room loads it): had it failed after, the server would have the cat in a
    // room the page can't show.
    const shown = await loadRoom(loading, async () => {
      const view = await stage();
      const snapshot = await request();
      // The admins' preview world draws rooms in their draft looks, any other world in the published ones: wherever the cat came from.
      await (await import("./looks")).syncLooks(snapshot.worldId);
      return show(snapshot, view);
    }, stay, (room) => !room.snapshot.notice);
    // Not where it was going (that room was full): where it landed instead, and why.
    if (shown.snapshot.notice) {
      overlay.toast(shown.snapshot.notice);
    }
  } catch (error) {
    // Still in `stay` if the server kept the cat there (a door that refused it, a Go to that couldn't); in no room
    // otherwise.
    room = stay;
    overlay.toast(messageOf(error));
  } finally {
    loads--;
  }
  leaveHomeIfSentOut();
}

// How many rooms are loading (taps go through pick; a rejoin doesn't).
let loads = 0;

/** The world list; `providers` when they're already known (a guest who just signed in from the sign-in panel). */
async function pickWorld(player: Player, providers?: string[], note?: Note): Promise<void> {
  me = player;
  lastShown = undefined;
  await connection.start();
  setChatMute(player.mute ?? null);
  // A guest can save their cat with any provider the server offers.
  const [saveWith, worlds] = await Promise.all([
    providers ?? (player.isGuest ? fetchProviders() : []),
    connection.getWorlds(),
    // Before any room: its chat history then comes without the lines of the cats the player muted.
    loadMutes(),
  ]);
  overlay.showWorlds(
    player,
    worlds,
    (worldId) => pick(() => enter(worldId)),
    () => void signOut().then(() => window.location.reload()),
    { providers: saveWith, link: (provider) => postTo(linkUrl(provider)) },
    note,
    {
      value: frameRate,
      change: (value) => {
        frameRate = value;
        try {
          localStorage.setItem(FRAME_RATE_KEY, value);
        } catch {
          // Storage refused (private browsing): the choice holds until the page closes.
        }
      },
    },
  );
  showNotice();
  // A warning left while the player was away: shown once, now.
  void showWarning();
}

async function start(): Promise<void> {
  const search = window.location.search;
  const player = await fetchMe();
  // How signing in or saving the cat went, said once: a reload doesn't say it again.
  const note = addressNote(search, player !== null);
  if (note) {
    window.history.replaceState(null, "", window.location.pathname);
  }

  // A banned player is shown why and until when, not the game (the server would refuse it anyway).
  if (player?.ban) {
    overlay.showBanned(player.ban);
    return;
  }

  // A sign-in refused because the network is banned: why and until when, as for a banned cat.
  if (!player && new URLSearchParams(search).get("signin") === "banned") {
    const ban = await fetchNetworkBan();
    if (ban) {
      overlay.showBanned(ban);
      return;
    }
  }

  if (player) {
    await pickWorld(player, undefined, note);
    return;
  }

  const providers = await fetchProviders();
  overlay.showSignIn(
    providers,
    (provider) => window.location.assign(loginUrl(provider)),
    () => void playAsGuest().then((guest) => pickWorld(guest, providers)).catch((error: unknown) => {
      if (error instanceof NetworkBannedError) {
        overlay.showBanned(error.ban);
      } else {
        overlay.toast(messageOf(error));
      }
    }),
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

// The card showing (or last shown), to show again as it changes (a mute from another tab).
let cardShown: PlayerCard | undefined;

function showCard(card: PlayerCard): void {
  cardShown = card;
  // "Visit home", unless the player is in that home already.
  const visit = canVisitFromCard(card, room?.snapshot.home?.ownerId) ? () => {
    overlay.closeDialog();
    visitHome(card.playerId);
  } : undefined;
  const outfit: Outfit = { fur: card.furColor, hat: card.hat ?? null, collar: card.collar ?? null };
  overlay.showPlayerCard(card, pictureOf(card.displayName, outfit, card.hatName, card.collarName),
    (action) => void changeFriendship(action, card.playerId), visit, mutes.has(card.playerId),
    (action) => safety(action, card),
    // An admin moderates from the card (the server sends its standing only to admins).
    card.moderation ? (request) => void moderate(card.playerId, card.displayName, request) : undefined);
}

/** Mute, Unmute or Report, from a cat's card. */
function safety(action: SafetyAction, card: PlayerCard): void {
  if (action === "report") {
    overlay.showReport(card.playerId, card.displayName, (draft) => void sendReport(card, draft), () => showCard(card));
  } else {
    void setMuted(card.playerId, card.displayName, action === "mute");
  }
}

/** The player's mutes, as the server has them now; a failure only leaves the ones known. */
async function loadMutes(): Promise<void> {
  try {
    applyMutes(await connection.getMutes());
  } catch (error) {
    appLog.warn("The muted players couldn't be read", error);
  }
}

/**
 * The player's mutes as they are now (changed in this tab or another): what newly muted cats said goes from the room and
 * from over them, and the card or friends list open shows it.
 */
function applyMutes(list: MutedView[]): void {
  const { added, changed } = mutes.set(list);
  for (const playerId of added) {
    room?.hush(playerId);
    scene?.hush(playerId);
  }
  if (!changed) {
    return;
  }
  const open = overlay.openDialogFor;
  if (open?.kind === "card" && cardShown?.playerId === open.playerId) {
    showCard(cardShown);
  } else if (open?.kind === "friends") {
    void openFriends(true);
  }
}

/** Mutes or unmutes a cat, and says so. */
async function setMuted(playerId: string, name: string, muted: boolean): Promise<void> {
  try {
    applyMutes(await connection.setMuted(playerId, muted));
    overlay.toast(muteMessage(name, muted));
  } catch (error) {
    overlay.toast(messageOf(error));
  }
}

// The cats a report is on its way about: a second tap on Send doesn't send it twice (a report about another cat goes).
const reporting = new Set<string>();

/** Sends the report form as filled in (or says what it still needs), then goes back to the cat's card. */
async function sendReport(card: PlayerCard, draft: ReportDraft): Promise<void> {
  const request = reportRequest(draft);
  if ("problem" in request) {
    overlay.toast(request.problem);
    return;
  }
  if (reporting.has(card.playerId)) {
    return;
  }
  reporting.add(card.playerId);
  try {
    const receipt = await connection.report(card.playerId, request);
    applyMutes(receipt.mutes);
    const open = overlay.openDialogFor;
    if (open?.kind === "report" && open.playerId === card.playerId) {
      showCard(card);
    }
    overlay.toast(reportSentMessage(card.displayName, receipt.muted, request.alsoMute));
  } catch (error) {
    overlay.toast(messageOf(error));
  } finally {
    reporting.delete(card.playerId);
  }
}

/**
 * A picture of a cat dressed in <outfit> for its card, described for a screen reader: the one taken already, or one
 * being taken (the room's scene takes them; none before a room has shown).
 */
function pictureOf(name: string, outfit: Outfit, hatName?: string | null, collarName?: string | null): CatPicture {
  const portraits = scene?.portraits;
  return {
    description: describeCat(name, outfit.fur, hatName, collarName),
    src: portraits ? portraits.cached(outfit) ?? portraits.picture(outfit) : undefined,
  };
}

/**
 * The friends list, with where each friend is now. A refresh (`refresh`) of the open list updates it in place, and does
 * nothing if the player closed it meanwhile.
 */
async function openFriends(refresh = false): Promise<void> {
  try {
    const list = await connection.getFriends();
    if (refresh && overlay.openDialogFor?.kind !== "friends") {
      return;
    }
    const home = room?.snapshot.home ?? undefined;
    const here = room ? { worldId: room.snapshot.worldId, roomId: room.snapshot.roomId, homeOwnerId: home?.ownerId } : undefined;
    overlay.showFriends(list, here, {
      card: (playerId) => void openCard(playerId),
      goTo: (playerId) => {
        overlay.closeDialog();
        // The server goes only once it can: a full world or room, or a friend gone, leaves the cat where it is.
        const stay = room;
        pick(() => load(() => connection.goToFriend(playerId), stay));
      },
      act: (action, playerId) => void changeFriendship(action, playerId),
      goHome: () => {
        overlay.closeDialog();
        const stay = room;
        pick(() => load(() => connection.goHome(), stay));
      },
      visitHome: (playerId) => {
        overlay.closeDialog();
        visitHome(playerId);
      },
      unmute: (playerId) => {
        const name = mutes.list.find((muted) => muted.playerId === playerId)?.displayName ?? "That cat";
        void setMuted(playerId, name, false);
      },
    }, isOwnHome(home, me?.id), mutes.list);
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
      await openFriends(true);
    }
  } catch (error) {
    overlay.toast(messageOf(error));
  }
}

/** An admin's action on another cat, from its card: the card as it is after, and what was done; or why not. */
async function moderate(playerId: string, name: string, request: ModerationRequest): Promise<void> {
  try {
    // The cat's own action first (unless a lift finds only its networks banned, or only its networks are to be banned),
    // then its networks'.
    let card = (request.action === "lift" && request.liftCat === false) || request.networksOnly
      ? undefined
      : await connection.moderate(playerId, request);
    if (request.action === "ban" && request.networks?.length) {
      card = await connection.banNetworks(playerId, request.networks, request.networkLength ?? "24h");
    } else if (request.action === "lift" && request.liftNetworks) {
      card = await connection.liftNetworkBans(playerId);
    }
    if (!card) {
      return;
    }
    const open = overlay.openDialogFor;
    if (open?.kind === "card" && open.playerId === playerId) {
      showCard(card);
    }
    overlay.toast(doneMessage(request, name));
  } catch (error) {
    overlay.toast(messageOf(error));
    // The cat's part may have been done before its networks' was refused: the card shows what holds now.
    const open = overlay.openDialogFor;
    if (open?.kind === "card" && open.playerId === playerId) {
      await connection.getPlayerCard(playerId).then(showCard, () => undefined);
    }
  }
}

/**
 * Another player changed their friendship with this one: the card or list showing it is brought up to date, and a new
 * request is said.
 */
async function friendsChanged(playerId: string): Promise<void> {
  const open = overlay.openDialogFor;
  if (open?.kind === "friends") {
    await openFriends(true);
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

/** The dialogs that show the player's side of the shop: the catalogue, and their own card (their wardrobe). */
type ShopSheet = "catalogue" | "own-card";

/**
 * Opens the catalogue or the player's own card, as the server has the shop now. A refresh (`refresh`) of the one open
 * updates it in place, and does nothing if the player closed it.
 */
async function openShopSheet(sheet: ShopSheet, refresh = false): Promise<void> {
  try {
    const shop = await connection.getShop();
    if (refresh && overlay.openDialogFor?.kind !== sheet) {
      return;
    }
    showShopSheet(sheet, shop);
  } catch (error) {
    overlay.toast(messageOf(error));
  }
}

/** The player's own card: a tap on their own cat (or "Your cat"), anywhere. */
function openOwnCard(): Promise<void> {
  return openShopSheet("own-card");
}

/** Walks the player's cat up to the shop's catalogue stand, and opens the catalogue once it's there. */
async function readCatalogue(): Promise<void> {
  const stand = catalogueStand(room?.snapshot.props);
  const cat = me && room?.cats.get(me.id);
  const view = scene;
  if (!stand || !cat || !me || !view) {
    return;
  }
  const id = me.id;
  // Walking off reels the line in, as any walk does.
  fishing.drop();
  try {
    // From where the cat is drawn, not where it was sent: near the stand on screen, the catalogue opens at once.
    await goRead(stand, view.positionOf(id) ?? cat, {
      walks: doorWalks,
      move: (x, z) => connection.move(x, z),
      reached: (x, z) => view.reached(id, x, z),
      open: () => openShopSheet("catalogue"),
    });
  } catch (error) {
    overlay.toast(messageOf(error));
  }
}

// The shop showing (in the catalogue or the player's own card), as last drawn, and how many of this tab's shop buttons
// are still waiting for the server (each draws the shop as it is after, so a CatDressed of this tab's own change, which
// comes back first, needs no fetch).
let shopShown: Shop | undefined;
let shopping = 0;

function showShopSheet(sheet: ShopSheet, shop: Shop): void {
  shopShown = shop;
  // Furniture just bought is there to put out.
  if (decorating) {
    decoratingShop = shop;
    drawDecorating();
  }
  if (me) {
    // The shop's count is the saved one: the room bar shows it too, and a world picked again starts from it.
    me = { ...me, coins: shop.coins };
    overlay.setCoins(me.coins);
  }
  const act = (action: ShopAction, itemId: string) => void shopAction(action, itemId, shop);
  if (sheet === "catalogue") {
    overlay.showCatalogue(shop, act);
    return;
  }
  const outfit: Outfit = { fur: shop.furColor, hat: shop.hat, collar: shop.collar };
  const nameOf = (id: string | null) => shop.items.find((item) => item.id === id)?.name;
  const rename: OwnCardRename = {
    editing: renamingOwnCard,
    start: startRename,
    cancel: cancelRename,
    save: (name) => void saveRename(name),
  };
  overlay.showOwnCard(
    me?.displayName ?? "Your cat", shop, pictureOf(me?.displayName ?? "Your cat", outfit, nameOf(shop.hat), nameOf(shop.collar)),
    act, rename, me?.id);
}

/** Buys, puts on or takes off an item, then shows the catalogue or card open as it is after, and says what happened. */
async function shopAction(action: ShopAction, itemId: string, before: Shop): Promise<void> {
  shopping++;
  try {
    const after = await connection.shop(action, itemId);
    const open = overlay.openDialogFor?.kind;
    if (open === "catalogue" || open === "own-card") {
      showShopSheet(open, after);
    } else if (me) {
      me = { ...me, coins: after.coins };
      overlay.setCoins(me.coins);
    }
    overlay.toast(shopMessage(action, before.items.find((item) => item.id === itemId)));
  } catch (error) {
    overlay.toast(messageOf(error));
  } finally {
    shopping--;
  }
}

/**
 * A cat changed what it wears: the card showing it (its picture too) is brought up to date; when it's the player's own,
 * the catalogue or their own card open, unless it shows that already (this tab's own change).
 */
async function dressed(outfit: OutfitView): Promise<void> {
  const open = overlay.openDialogFor;
  if ((open?.kind === "catalogue" || open?.kind === "own-card") && outfit.playerId === me?.id) {
    if (shopping > 0 || (shopShown && showsOutfit(shopShown, outfit))) {
      return;
    }
    await openShopSheet(open.kind, true);
  } else {
    await refreshCard(outfit.playerId);
  }
}

/** The player card over the room, if it's open and showing <playerId>, brought up to date (a name, an outfit). */
async function refreshCard(playerId: string): Promise<void> {
  const open = overlay.openDialogFor;
  if (open?.kind !== "card" || open.playerId !== playerId) {
    return;
  }

  try {
    const card = await connection.getPlayerCard(playerId);
    const now = overlay.openDialogFor;
    if (now?.kind === "card" && now.playerId === playerId) {
      showCard(card);
    }
  } catch {
    // Only a refresh: the card shows it when opened again.
  }
}

/** Opens the inline Rename box on the player's own card. */
function startRename(): void {
  renamingOwnCard = true;
  if (shopShown) {
    showShopSheet("own-card", shopShown);
  }
}

/** Closes the own card's inline Rename box without saving. */
function cancelRename(): void {
  renamingOwnCard = false;
  if (shopShown) {
    showShopSheet("own-card", shopShown);
  }
}

/** Saves the name typed into the own card's Rename box; stays open, with what was typed, if the server refuses it. */
async function saveRename(name: string): Promise<void> {
  try {
    const displayName = await connection.rename(name);
    if (me) {
      me = { ...me, displayName };
    }
    renamingOwnCard = false;
    if (shopShown) {
      showShopSheet("own-card", shopShown);
    }
  } catch (error) {
    overlay.toast(messageOf(error));
  }
}

/**
 * Starts decorating the player's own home: the room takes taps for its furniture (scene.editing), and a panel takes the
 * place of the chat with the furniture the player has, from the shop.
 */
async function startDecorating(): Promise<void> {
  const home = room?.snapshot.home;
  if (!home || !isOwnHome(home, me?.id) || decorating) {
    return;
  }
  try {
    decoratingShop = await connection.getShop();
  } catch (error) {
    overlay.toast(messageOf(error));
    return;
  }
  // Not if the cat left the home meanwhile.
  if (room?.snapshot.home?.ownerId !== home.ownerId) {
    return;
  }
  decorating = {};
  if (scene) {
    scene.editing = true;
  }
  drawDecorating();
}

/** Shows the decorating panel and the ring round the piece selected as they are now. */
function drawDecorating(): void {
  const home = room?.snapshot.home;
  const shop = decoratingShop;
  if (!decorating || !home || !shop) {
    return;
  }
  const left = furnitureLeft(shop, home);
  // A piece put away (in another tab, say) is no longer selected; furniture with none left to put out, no longer picked.
  const selected = home.furniture.find((piece) => piece.id === decorating?.selected);
  const picked = left.find((row) => row.item.id === decorating?.picked && row.left > 0)?.item.id;
  decorating = { selected: selected?.id, picked: selected ? undefined : picked };
  scene?.ring(selected ? propOf(selected) : undefined);
  overlay.showDecorating(
    { hint: decoratingHint(decorating, left, home), left, placed: home.furniture, picked: decorating.picked, selected },
    decorateActions,
  );
}

const decorateActions: DecorateActions = {
  pick: (itemId) => {
    decorating = { picked: decorating?.picked === itemId ? undefined : itemId };
    drawDecorating();
  },
  putAnywhere: (itemId) => void putOut(itemId, undefined),
  select: (pieceId) => {
    decorating = { selected: decorating?.selected === pieceId ? undefined : pieceId };
    drawDecorating();
  },
  turn: (pieceId) => void changeHome(() => connection.turnFurniture(pieceId)),
  putAway: (pieceId) => void changeHome(() => connection.putAwayFurniture(pieceId)),
  done: () => stopDecorating(),
};

function stopDecorating(): void {
  decorating = undefined;
  decoratingShop = undefined;
  if (scene) {
    scene.editing = false;
    scene.ring(undefined);
  }
  overlay.stopDecorating();
}

/**
 * A tap on the room while decorating: on a piece of furniture, selects it; on the floor, puts the furniture picked
 * there, or (with none picked) drops the selection.
 */
function editTap(x: number, z: number): void {
  const home = room?.snapshot.home;
  if (!decorating || !home) {
    return;
  }
  const piece = pieceAt(home.furniture, x, z);
  if (piece) {
    decorating = { selected: piece.id };
    drawDecorating();
  } else if (decorating.picked) {
    void putOut(decorating.picked, { x, z });
  } else {
    decorating = {};
    drawDecorating();
  }
}

/** Puts out a piece of the furniture <itemId> at <spot> (or wherever it fits), then selects it, to turn it. */
async function putOut(itemId: string, spot: { x: number; z: number } | undefined): Promise<void> {
  const before = new Set(room?.snapshot.home?.furniture.map((piece) => piece.id));
  const home = await changeHome(() => connection.placeFurniture(itemId, spot));
  const added = home?.furniture.find((piece) => !before.has(piece.id));
  if (added && decorating) {
    decorating = { selected: added.id };
    drawDecorating();
  }
}

/** Changes the player's home (the server checks the change), then shows it as it is after; or says why not. */
async function changeHome(change: () => Promise<HomeView>): Promise<HomeView | undefined> {
  try {
    const home = await change();
    applyHome(home);
    return home;
  } catch (error) {
    overlay.toast(messageOf(error));
    return undefined;
  }
}

/** A home as its owner just changed it: drawn, if it is the room showing, and the decorating panel brought up to date. */
function applyHome(home: HomeView, here: Room | undefined = room): void {
  if (here?.furnish(home)) {
    if (here === room) {
      scene?.furnish(propsOf(home));
    }
    overlay.setVisitors(home.visitors);
  }
  drawDecorating();
}

/** Changes who may visit the player's home; whoever may no longer stay is sent out. */
async function letIn(visitors: HomeVisitors): Promise<void> {
  const home = await changeHome(() => connection.letIntoHome(visitors));
  if (home) {
    overlay.toast(visitorsMessage(home.visitors));
  } else if (room?.snapshot.home) {
    overlay.setVisitors(room.snapshot.home.visitors);
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

function readFrameRate(): FrameRate {
  try {
    return savedFrameRate(localStorage);
  } catch {
    return "auto";
  }
}

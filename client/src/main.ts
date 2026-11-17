import { Engine } from "@babylonjs/core";
import { addressNote, linkUrl } from "./link";
import { GameConnection, fetchMe, fetchProviders, messageOf, playAsGuest, signOut } from "./net";
import { PendingEvents } from "./pending";
import { comeBack } from "./rejoin";
import { Room } from "./room";
import { RoomScene } from "./scene";
import { Overlay, type Note } from "./ui";
import type { Player, RoomSnapshot } from "./types";

const overlay = new Overlay(required<HTMLElement>("#ui"));
const engine = new Engine(required<HTMLCanvasElement>("#game"), true, { stencil: true });
window.addEventListener("resize", () => engine.resize());
const scene = new RoomScene(engine);

let room: Room | undefined;
// Who is playing, once known: the world picker needs it again when a world is lost.
let me: Player | undefined;
// Room events that arrive while the next room loads, for it once its snapshot shows.
const loading = new PendingEvents<(room: Room) => void>();

// Read-only: the end-to-end tests (e2e/) read the room here, since the cats and chat are drawn on a canvas.
declare global {
  interface Window {
    pspsps?: { room(): Room | undefined };
  }
}
window.pspsps = { room: () => room };

const connection = new GameConnection({
  catJoined: (cat) => onRoom((here) => {
    here.join(cat);
    scene.place(cat);
  }),
  catLeft: (playerId) => onRoom((here) => {
    here.leave(playerId);
    scene.remove(playerId);
  }),
  catMoved: (playerId, x, z) => onRoom((here) => {
    if (here.move(playerId, x, z)) {
      scene.walk(playerId, x, z);
    }
  }),
  chatSaid: (line) => onRoom((here) => {
    if (here.say(line)) {
      scene.speak(line.playerId, line.text);
    }
  }),
  catEmoted: (emote) => onRoom((here) => {
    here.emote(emote);
    scene.emote(emote.playerId, emote.emote);
  }),
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
});

/** Applies a room event to the room showing, or holds it for the one loading. */
function onRoom(apply: (room: Room) => void): void {
  if (!loading.hold(apply) && room) {
    apply(room);
  }
}

scene.onFloorClicked((x, z) => {
  if (room) {
    connection.move(x, z).catch((error: unknown) => overlay.toast(messageOf(error)));
  }
});

function show(snapshot: RoomSnapshot): Room {
  const shown = new Room(snapshot);
  room = shown;
  scene.show(snapshot);
  overlay.showRoom(
    snapshot,
    (roomId) => void goTo(roomId),
    (text) => connection.say(text).catch((error: unknown) => overlay.toast(messageOf(error))),
    // Between rooms (room undefined while the next one loads) an emote would only be refused.
    (emote) => {
      if (room) {
        connection.emote(emote).catch((error: unknown) => overlay.toast(messageOf(error)));
      }
    },
    () => window.location.reload(),
  );
  return shown;
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
    shown = show(await request());
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
    (worldId) => void enter(worldId),
    () => void signOut().then(() => window.location.reload()),
    { providers: saveWith, link: (provider) => window.location.assign(linkUrl(provider)) },
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
    (provider) => window.location.assign(`/auth/login/${encodeURIComponent(provider)}`),
    () => void playAsGuest().then((guest) => pickWorld(guest, providers)).catch((error: unknown) => overlay.toast(messageOf(error))),
    note,
  );
}

function required<T extends Element>(selector: string): T {
  const element = document.querySelector<T>(selector);
  if (!element) {
    throw new Error(`index.html has no ${selector}`);
  }
  return element;
}

void start();

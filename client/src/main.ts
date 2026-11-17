import { Engine } from "@babylonjs/core";
import { addressNote, linkUrl } from "./link";
import { GameConnection, fetchMe, fetchProviders, messageOf, playAsGuest, signOut } from "./net";
import { Room } from "./room";
import { RoomScene } from "./scene";
import { Overlay, type Note } from "./ui";
import type { Player, RoomSnapshot } from "./types";

const overlay = new Overlay(required<HTMLElement>("#ui"));
const engine = new Engine(required<HTMLCanvasElement>("#game"), true, { stencil: true });
window.addEventListener("resize", () => engine.resize());
const scene = new RoomScene(engine);

let room: Room | undefined;

// Read-only: the end-to-end tests (e2e/) read the room here, since the cats and chat are drawn on a canvas.
declare global {
  interface Window {
    pspsps?: { room(): Room | undefined };
  }
}
window.pspsps = { room: () => room };

const connection = new GameConnection({
  catJoined: (cat) => {
    if (room) {
      room.join(cat);
      scene.place(cat);
    }
  },
  catLeft: (playerId) => {
    if (room) {
      room.leave(playerId);
      scene.remove(playerId);
    }
  },
  catMoved: (playerId, x, z) => {
    if (room?.move(playerId, x, z)) {
      scene.walk(playerId, x, z);
    }
  },
  chatSaid: (line) => {
    if (room) {
      room.say(line);
      scene.speak(line.playerId, line.text);
    }
  },
  replaced: () => {
    room = undefined;
    overlay.showReplaced(() => window.location.reload());
  },
  reconnected: () => {
    // The server dropped us from the room with the old connection: walk back in.
    const current = room?.snapshot;
    if (current) {
      void enter(current.worldId).then(() => current.roomId === room?.snapshot.roomId ? undefined : goTo(current.roomId));
    }
  },
});

scene.onFloorClicked((x, z) => {
  if (room) {
    connection.move(x, z).catch((error: unknown) => overlay.toast(messageOf(error)));
  }
});

function show(snapshot: RoomSnapshot): void {
  room = new Room(snapshot);
  scene.show(snapshot);
  overlay.showRoom(
    snapshot,
    (roomId) => void goTo(roomId),
    (text) => connection.say(text).catch((error: unknown) => overlay.toast(messageOf(error))),
    () => window.location.reload(),
  );
}

async function enter(worldId: string): Promise<void> {
  // The server announces our own join (and anything else in the new room) before the snapshot arrives; the snapshot
  // already has it all, so nothing is applied to the old room meanwhile.
  room = undefined;
  try {
    show(await connection.enterWorld(worldId));
  } catch (error) {
    overlay.toast(messageOf(error));
  }
}

async function goTo(roomId: string): Promise<void> {
  room = undefined;
  try {
    show(await connection.goToRoom(roomId));
  } catch (error) {
    overlay.toast(messageOf(error));
  }
}

/** The world list; `providers` when they're already known (a guest who just signed in from the sign-in panel). */
async function pickWorld(player: Player, providers?: string[], note?: Note): Promise<void> {
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

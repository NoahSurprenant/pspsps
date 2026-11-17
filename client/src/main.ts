import { Engine } from "@babylonjs/core";
import { GameConnection, fetchMe, fetchProviders, messageOf, playAsGuest, signOut } from "./net";
import { PendingEvents } from "./pending";
import { walkBackIn } from "./rejoin";
import { Room } from "./room";
import { RoomScene } from "./scene";
import { Overlay } from "./ui";
import type { Player, RoomSnapshot } from "./types";

const overlay = new Overlay(required<HTMLElement>("#ui"));
const engine = new Engine(required<HTMLCanvasElement>("#game"), true, { stencil: true });
window.addEventListener("resize", () => engine.resize());
const scene = new RoomScene(engine);

let room: Room | undefined;
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
  reconnected: () => rejoin(),
  // The room no longer has our cat: walk back in the same way.
  roomLost: () => rejoin(),
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

function rejoin(): void {
  const current = room?.snapshot;
  if (current) {
    void walkBackIn(
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

async function pickWorld(player: Player): Promise<void> {
  await connection.start();
  overlay.showWorlds(player, await connection.getWorlds(), (worldId) => void enter(worldId), () => {
    void signOut().then(() => window.location.reload());
  });
}

async function start(): Promise<void> {
  const player = await fetchMe();
  if (player) {
    await pickWorld(player);
    return;
  }

  const failed = new URLSearchParams(window.location.search).get("signin") === "failed";
  overlay.showSignIn(
    await fetchProviders(),
    (provider) => window.location.assign(`/auth/login/${encodeURIComponent(provider)}`),
    () => void playAsGuest().then(pickWorld).catch((error: unknown) => overlay.toast(messageOf(error))),
    failed ? "Signing in didn't work. Try again." : undefined,
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

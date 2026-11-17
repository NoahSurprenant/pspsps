import { Engine } from "@babylonjs/core";
import { GameConnection, fetchMe, fetchProviders, messageOf, playAsGuest, signOut } from "./net";
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
  // The server dropped us from the room with the old connection: walk back in.
  reconnected: () => rejoin(),
  // The room no longer has our cat: walk back in the same way.
  roomLost: () => rejoin(),
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

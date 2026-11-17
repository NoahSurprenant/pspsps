import { Engine } from "@babylonjs/core";
import { GameConnection, fetchMe, fetchProviders, messageOf, playAsGuest, signOut } from "./net";
import { Room } from "./room";
import { RoomScene } from "./scene";
import { Overlay } from "./ui";
import type { Player, RoomSnapshot } from "./types";

const overlay = new Overlay(required<HTMLElement>("#ui"));
const engine = new Engine(required<HTMLCanvasElement>("#game"), true, { stencil: true });
window.addEventListener("resize", () => engine.resize());
const scene = new RoomScene(engine);

let room: Room | undefined;

const connection = new GameConnection({
  catJoined: (cat) => {
    room?.join(cat);
    scene.place(cat);
  },
  catLeft: (playerId) => {
    room?.leave(playerId);
    scene.remove(playerId);
  },
  catMoved: (playerId, x, z) => {
    if (room?.move(playerId, x, z)) {
      scene.walk(playerId, x, z);
    }
  },
  chatSaid: (line) => {
    room?.say(line);
    scene.speak(line.playerId, line.text);
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
  try {
    show(await connection.enterWorld(worldId));
  } catch (error) {
    overlay.toast(messageOf(error));
  }
}

async function goTo(roomId: string): Promise<void> {
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

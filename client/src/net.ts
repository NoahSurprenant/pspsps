import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from "@microsoft/signalr";
import type { CatView, ChatView, Player, RoomSnapshot, WorldSummary } from "./types";

export interface GameEvents {
  catJoined(cat: CatView): void;
  catLeft(playerId: string): void;
  catMoved(playerId: string, x: number, z: number): void;
  chatSaid(line: ChatView): void;
  /** The connection came back after a drop; the server forgot where we were, so the game re-enters. */
  reconnected(): void;
  /** The room lost our cat (its lease ran out, or the room restarted); the game walks back in. */
  roomLost(): void;
  /** The player opened the game in another tab, which plays now; this one has stopped. */
  replaced(): void;
}

export async function fetchMe(): Promise<Player | null> {
  // 200 with the player, or 204 when not signed in.
  const response = await fetch("/api/me");
  return response.status === 200 ? ((await response.json()) as Player) : null;
}

export async function fetchProviders(): Promise<string[]> {
  const response = await fetch("/auth/providers");
  return response.ok ? ((await response.json()) as string[]) : [];
}

export async function playAsGuest(): Promise<Player> {
  const response = await fetch("/auth/guest", { method: "POST" });
  if (!response.ok) {
    throw new Error("Couldn't make a guest cat. Try again.");
  }
  return (await response.json()) as Player;
}

export async function signOut(): Promise<void> {
  await fetch("/auth/logout", { method: "POST" });
}

/** The one realtime connection (src/Pspsps.Server/Hubs/GameHub.cs). */
export class GameConnection {
  private readonly hub: HubConnection;

  constructor(events: GameEvents) {
    this.hub = new HubConnectionBuilder()
      .withUrl("/hubs/game")
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();
    this.hub.on("CatJoined", (cat: CatView) => events.catJoined(cat));
    this.hub.on("CatLeft", (playerId: string) => events.catLeft(playerId));
    this.hub.on("CatMoved", (playerId: string, x: number, z: number) => events.catMoved(playerId, x, z));
    this.hub.on("ChatSaid", (line: ChatView) => events.chatSaid(line));
    this.hub.onreconnected(() => events.reconnected());
    this.hub.on("RoomLost", () => events.roomLost());
    this.hub.on("SessionReplaced", () => {
      void this.hub.stop().then(() => events.replaced());
    });
  }

  async start(): Promise<void> {
    if (this.hub.state === HubConnectionState.Disconnected) {
      await this.hub.start();
    }
  }

  getWorlds(): Promise<WorldSummary[]> {
    return this.hub.invoke<WorldSummary[]>("GetWorlds");
  }

  enterWorld(worldId: string): Promise<RoomSnapshot> {
    return this.hub.invoke<RoomSnapshot>("EnterWorld", worldId);
  }

  goToRoom(roomId: string): Promise<RoomSnapshot> {
    return this.hub.invoke<RoomSnapshot>("GoToRoom", roomId);
  }

  move(x: number, z: number): Promise<void> {
    return this.hub.invoke("Move", x, z);
  }

  say(text: string): Promise<void> {
    return this.hub.invoke("Say", text);
  }
}

/** The server's message from a refused call, without SignalR's "An unexpected error occurred invoking..." prefix. */
export function messageOf(error: unknown): string {
  const text = error instanceof Error ? error.message : String(error);
  const marker = "HubException: ";
  const at = text.lastIndexOf(marker);
  return at >= 0 ? text.slice(at + marker.length) : text;
}

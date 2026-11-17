import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from "@microsoft/signalr";
import type { FriendAction } from "./friends";
import type {
  CatchView, CatView, ChatView, Emote, EmoteView, Friend, Player, PlayerCard, ReelView, RoomSnapshot, WorldSummary,
} from "./types";

export interface GameEvents {
  catJoined(cat: CatView): void;
  catLeft(playerId: string): void;
  catMoved(playerId: string, x: number, z: number): void;
  chatSaid(line: ChatView): void;
  catEmoted(emote: EmoteView): void;
  /** Something bit our line: reel now. */
  fishBit(): void;
  fishGotAway(): void;
  /** A cat in the room caught something. */
  catFished(caught: CatchView): void;
  /** The connection came back after a drop; the server forgot where we were, so the game re-enters. */
  reconnected(): void;
  /** The room lost our cat (its lease ran out, or the room restarted); the game walks back in. */
  roomLost(): void;
  /** The world no longer has room for us (it restarted and filled up); the game goes back to the world picker. */
  worldLost(message: string): void;
  /** The player opened the game in another tab, which plays now; this one has stopped. */
  replaced(): void;
  /** Another player asked this one to be friends, answered, took a request back or ended a friendship. */
  friendsChanged(playerId: string): void;
}

export async function fetchMe(): Promise<Player | null> {
  // 200 with the player, or 204 when not signed in.
  const response = await fetch("/api/me");
  return response.status === 200 ? ((await response.json()) as Player) : null;
}

/** The configured providers; none when the request fails, which only hides the sign-in and save buttons. */
export async function fetchProviders(): Promise<string[]> {
  try {
    const response = await fetch("/auth/providers");
    return response.ok ? ((await response.json()) as string[]) : [];
  } catch {
    return [];
  }
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

/** The hub method for each change to a friendship. */
export const friendMethods: Record<FriendAction, string> = {
  add: "AddFriend",
  accept: "AcceptFriend",
  decline: "DeclineFriend",
  cancel: "CancelFriendRequest",
  remove: "RemoveFriend",
};

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
    this.hub.on("CatEmoted", (emote: EmoteView) => events.catEmoted(emote));
    this.hub.on("FishBit", () => events.fishBit());
    this.hub.on("FishGotAway", () => events.fishGotAway());
    this.hub.on("CatFished", (caught: CatchView) => events.catFished(caught));
    this.hub.onreconnected(() => events.reconnected());
    this.hub.on("RoomLost", () => events.roomLost());
    this.hub.on("WorldLost", (message: string) => events.worldLost(message));
    this.hub.on("FriendsChanged", (playerId: string) => events.friendsChanged(playerId));
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

  cast(): Promise<void> {
    return this.hub.invoke("Cast");
  }

  reel(): Promise<ReelView> {
    return this.hub.invoke<ReelView>("Reel");
  }

  emote(emote: Emote): Promise<void> {
    return this.hub.invoke("Emote", emote);
  }

  getPlayerCard(playerId: string): Promise<PlayerCard> {
    return this.hub.invoke<PlayerCard>("GetPlayerCard", playerId);
  }

  /** A change to the friendship with another cat; the card as it is after. */
  changeFriendship(action: FriendAction, playerId: string): Promise<PlayerCard> {
    return this.hub.invoke<PlayerCard>(friendMethods[action], playerId);
  }

  getFriends(): Promise<Friend[]> {
    return this.hub.invoke<Friend[]>("GetFriends");
  }

  /** Goes to the friend's room, in whichever world it is. */
  goToFriend(playerId: string): Promise<RoomSnapshot> {
    return this.hub.invoke<RoomSnapshot>("GoToFriend", playerId);
  }
}

/** The server's message from a refused call, without SignalR's "An unexpected error occurred invoking..." prefix. */
export function messageOf(error: unknown): string {
  const text = error instanceof Error ? error.message : String(error);
  const marker = "HubException: ";
  const at = text.lastIndexOf(marker);
  return at >= 0 ? text.slice(at + marker.length) : text;
}

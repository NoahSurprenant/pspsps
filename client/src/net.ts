import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from "@microsoft/signalr";
import type { FriendAction } from "./friends";
import { appLog, hubTracer, serverFetch } from "./telemetry";
import type { ReportRequest } from "./safety";
import type { ShopAction } from "./shop";
import type {
  CatchView, CatView, ChatView, Emote, EmoteView, Friend, HomeView, HomeVisitors, MutedView, OutfitView, Player, PlayerCard,
  ReelView, ReportReceipt, RoomSnapshot, Shop, WorldSummary,
} from "./types";

export interface GameEvents {
  catJoined(cat: CatView): void;
  catLeft(playerId: string): void;
  catMoved(playerId: string, x: number, z: number): void;
  chatSaid(line: ChatView): void;
  catEmoted(emote: EmoteView): void;
  /** A cat in the room put on or took off a hat or collar, or changed its fur. */
  catDressed(outfit: OutfitView): void;
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
  /** The owner of the home our cat is in changed its furniture, or who may visit. */
  homeChanged(home: HomeView): void;
  /** Our cat was sent out of <ownerId>'s home (its owner no longer lets it in): go back to Town Square. */
  sentOut(ownerId: string, message: string): void;
  /** The player muted or unmuted someone, from this tab or another: everyone muted now. */
  mutesChanged(mutes: MutedView[]): void;
}

export async function fetchMe(): Promise<Player | null> {
  // 200 with the player, or 204 when not signed in.
  const response = await serverFetch("/api/me");
  return response.status === 200 ? ((await response.json()) as Player) : null;
}

/** The configured providers; none when the request fails, which only hides the sign-in and save buttons. */
export async function fetchProviders(): Promise<string[]> {
  try {
    const response = await serverFetch("/auth/providers");
    return response.ok ? ((await response.json()) as string[]) : [];
  } catch {
    return [];
  }
}

export async function playAsGuest(): Promise<Player> {
  const response = await serverFetch("/auth/guest", { method: "POST" });
  if (!response.ok) {
    throw new Error("Couldn't make a guest cat. Try again.");
  }
  return (await response.json()) as Player;
}

export async function signOut(): Promise<void> {
  await serverFetch("/auth/logout", { method: "POST" });
}

/** The hub method for each change to a friendship. */
export const friendMethods: Record<FriendAction, string> = {
  add: "AddFriend",
  accept: "AcceptFriend",
  decline: "DeclineFriend",
  cancel: "CancelFriendRequest",
  remove: "RemoveFriend",
};

/** The hub method for each shop button. */
export const shopMethods: Record<ShopAction, string> = {
  buy: "Buy",
  wear: "Wear",
  takeOff: "TakeOff",
};

/**
 * The one realtime connection (src/Pspsps.Server/Hubs/GameHub.cs). With telemetry on, each call is traced (its trace
 * context in the call's headers, which the server's hub span continues), and the connection's life is logged: up,
 * dropped and reconnecting, back, closed, and taken over by another tab.
 */
export class GameConnection {
  private readonly hub: HubConnection;

  constructor(events: GameEvents) {
    this.hub = new HubConnectionBuilder()
      .withUrl("/hubs/game")
      .withHubProtocol(hubTracer.protocol)
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();
    this.hub.onreconnecting((error) => appLog.warn("The hub connection dropped; reconnecting", error));
    // Closed for good: stop() (a tab taken over), or a drop the reconnects couldn't get past.
    this.hub.onclose((error) => error === undefined
      ? appLog.info("The hub connection closed")
      : appLog.warn("The hub connection closed", error));
    this.hub.on("CatJoined", (cat: CatView) => events.catJoined(cat));
    this.hub.on("CatLeft", (playerId: string) => events.catLeft(playerId));
    this.hub.on("CatMoved", (playerId: string, x: number, z: number) => events.catMoved(playerId, x, z));
    this.hub.on("ChatSaid", (line: ChatView) => events.chatSaid(line));
    this.hub.on("CatEmoted", (emote: EmoteView) => events.catEmoted(emote));
    this.hub.on("CatDressed", (outfit: OutfitView) => events.catDressed(outfit));
    this.hub.on("FishBit", () => events.fishBit());
    this.hub.on("FishGotAway", () => events.fishGotAway());
    this.hub.on("CatFished", (caught: CatchView) => events.catFished(caught));
    this.hub.onreconnected(() => {
      appLog.info("The hub connection is back");
      events.reconnected();
    });
    this.hub.on("RoomLost", () => events.roomLost());
    this.hub.on("WorldLost", (message: string) => events.worldLost(message));
    this.hub.on("FriendsChanged", (playerId: string) => events.friendsChanged(playerId));
    this.hub.on("HomeChanged", (home: HomeView) => events.homeChanged(home));
    this.hub.on("SentOut", (ownerId: string, message: string) => events.sentOut(ownerId, message));
    this.hub.on("MutesChanged", (mutes: MutedView[]) => events.mutesChanged(mutes));
    this.hub.on("SessionReplaced", () => {
      appLog.info("Joined from another tab: this one stopped");
      void this.hub.stop().then(() => events.replaced());
    });
  }

  async start(): Promise<void> {
    if (this.hub.state === HubConnectionState.Disconnected) {
      try {
        await this.hub.start();
      } catch (error) {
        appLog.error("The hub connection failed", error);
        throw error;
      }
      appLog.info("The hub connection is up");
    }
  }

  /** Calls a hub method, traced when telemetry is on. */
  private call<T = void>(method: string, ...args: unknown[]): Promise<T> {
    return hubTracer.invoke(method, () => this.hub.invoke<T>(method, ...args));
  }

  getWorlds(): Promise<WorldSummary[]> {
    return this.call<WorldSummary[]>("GetWorlds");
  }

  enterWorld(worldId: string): Promise<RoomSnapshot> {
    return this.call<RoomSnapshot>("EnterWorld", worldId);
  }

  goToRoom(roomId: string): Promise<RoomSnapshot> {
    return this.call<RoomSnapshot>("GoToRoom", roomId);
  }

  /** Goes through a door the cat stands on, into the room it leads to. */
  goThroughDoor(doorId: string): Promise<RoomSnapshot> {
    return this.call<RoomSnapshot>("GoThroughDoor", doorId);
  }

  move(x: number, z: number): Promise<void> {
    return this.call("Move", x, z);
  }

  say(text: string): Promise<void> {
    return this.call("Say", text);
  }

  cast(): Promise<void> {
    return this.call("Cast");
  }

  reel(): Promise<ReelView> {
    return this.call<ReelView>("Reel");
  }

  emote(emote: Emote): Promise<void> {
    return this.call("Emote", emote);
  }

  getPlayerCard(playerId: string): Promise<PlayerCard> {
    return this.call<PlayerCard>("GetPlayerCard", playerId);
  }

  /** A change to the friendship with another cat; the card as it is after. */
  changeFriendship(action: FriendAction, playerId: string): Promise<PlayerCard> {
    return this.call<PlayerCard>(friendMethods[action], playerId);
  }

  getShop(): Promise<Shop> {
    return this.call<Shop>("GetShop");
  }

  /** Buys, puts on or takes off a shop item; the shop as it is after. */
  shop(action: ShopAction, itemId: string): Promise<Shop> {
    return this.call<Shop>(shopMethods[action], itemId);
  }

  getFriends(): Promise<Friend[]> {
    return this.call<Friend[]>("GetFriends");
  }

  /** The players this one has muted. */
  getMutes(): Promise<MutedView[]> {
    return this.call<MutedView[]>("GetMutes");
  }

  /** Mutes (or unmutes) a cat; everyone muted after. */
  setMuted(playerId: string, muted: boolean): Promise<MutedView[]> {
    return this.call<MutedView[]>(muted ? "Mute" : "Unmute", playerId);
  }

  /** Reports a cat (the server adds what was said in the room), and mutes it too with `request.alsoMute`. */
  report(playerId: string, request: ReportRequest): Promise<ReportReceipt> {
    return this.call<ReportReceipt>("Report", playerId, request.reason, request.note, request.alsoMute);
  }

  /** Goes to the friend's room, in whichever world it is. */
  goToFriend(playerId: string): Promise<RoomSnapshot> {
    return this.call<RoomSnapshot>("GoToFriend", playerId);
  }

  /** Goes into the player's own home. */
  goHome(): Promise<RoomSnapshot> {
    return this.call<RoomSnapshot>("GoHome");
  }

  /** Goes into another player's home, if they let this player in. */
  visitHome(ownerId: string): Promise<RoomSnapshot> {
    return this.call<RoomSnapshot>("VisitHome", ownerId);
  }

  /** Puts out a piece of the player's furniture at (x, z), or with no spot wherever it fits; the home as it is after. */
  placeFurniture(itemId: string, spot: { x: number; z: number } | undefined, quarterTurns = 0): Promise<HomeView> {
    return this.call<HomeView>("PlaceFurniture", itemId, spot?.x ?? null, spot?.z ?? null, quarterTurns);
  }

  turnFurniture(pieceId: string): Promise<HomeView> {
    return this.call<HomeView>("TurnFurniture", pieceId);
  }

  putAwayFurniture(pieceId: string): Promise<HomeView> {
    return this.call<HomeView>("PutAwayFurniture", pieceId);
  }

  /** Changes who may visit the player's home; whoever may no longer stay is sent out. */
  letIntoHome(visitors: HomeVisitors): Promise<HomeView> {
    return this.call<HomeView>("LetIntoHome", visitors);
  }
}

/** The server's message from a refused call, without SignalR's "An unexpected error occurred invoking..." prefix. */
export function messageOf(error: unknown): string {
  const text = error instanceof Error ? error.message : String(error);
  const marker = "HubException: ";
  const at = text.lastIndexOf(marker);
  return at >= 0 ? text.slice(at + marker.length) : text;
}

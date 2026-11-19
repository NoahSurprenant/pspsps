import {
  HttpTransportType, HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection, type IHttpConnectionOptions,
} from "@microsoft/signalr";
import type { FriendAction } from "./friends";
import { appLog, hubTracer, serverFetch } from "./telemetry";
import type { ModerationRequest, Sanction, Warning } from "./moderation";
import type { ReportRequest } from "./safety";
import type { ShopAction } from "./shop";
import type {
  CatchView, CatView, ChatView, Emote, EmoteView, Friend, HomeView, HomeVisitors, MutedView, OutfitView, Player, PlayerCard,
  ReelView, RenameView, ReportReceipt, RoomSnapshot, Shop, WorldSummary,
} from "./types";

export interface GameEvents {
  catJoined(cat: CatView): void;
  catLeft(playerId: string): void;
  catMoved(playerId: string, x: number, z: number): void;
  chatSaid(line: ChatView): void;
  catEmoted(emote: EmoteView): void;
  /** A cat in the room put on or took off a hat or collar, or changed its fur. */
  catDressed(outfit: OutfitView): void;
  /** A cat in the room was renamed. */
  catRenamed(rename: RenameView): void;
  /** This room is a home whose owner (not necessarily our cat) was renamed: shown as their new name's home. */
  homeRenamed(ownerName: string): void;
  /** We renamed our own cat, from this tab or another: the own card and anywhere else we show our name. */
  renamed(displayName: string): void;
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
  /** An admin banned the player: this tab has stopped; show why and until when. */
  banned(ban: Sanction): void;
  /** An admin muted the player (why, until when), or lifted it (null). */
  muted(mute: Sanction | null): void;
  /** An admin left the player a warning: fetch the warnings (getWarnings) and show them. */
  warned(): void;
  /** The connection closed and couldn't come back (not this tab stopping it): the server may have refused it. */
  closed(): void;
  /** The connection dropped, and the tab is trying to come back: a banned player is refused, and is told why. */
  reconnecting(): void;
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

/** A guest or sign-in refused because the network the player is on is banned: the ban, to show as a banned cat's. */
export class NetworkBannedError extends Error {
  readonly ban: Sanction;

  constructor(ban: Sanction) {
    super("This network is banned.");
    this.ban = ban;
  }
}

export async function playAsGuest(): Promise<Player> {
  const response = await serverFetch("/auth/guest", { method: "POST" });
  if (response.status === 403) {
    // The network is banned (Contracts.cs, BannedView): said as a banned cat's is.
    const refused = (await response.json().catch(() => null)) as { ban?: Sanction } | null;
    if (refused?.ban) {
      throw new NetworkBannedError(refused.ban);
    }
  }
  if (!response.ok) {
    throw new Error("Couldn't make a guest cat. Try again.");
  }
  return (await response.json()) as Player;
}

/** The ban on the network the page is on (asked after a sign-in was refused for it), or null. */
export async function fetchNetworkBan(): Promise<Sanction | null> {
  try {
    const response = await serverFetch("/auth/ban");
    return response.status === 200 ? ((await response.json()) as Sanction) : null;
  } catch {
    return null;
  }
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
 * How the hub connects, in order. First a WebSocket straight away, with no negotiate request: everything the connection
 * sends goes over that one socket, so it stays on the pod it opened on, and several pods behind one Service need no
 * sticky sessions (ASP.NET Core's "SignalR hosting and scaling": sticky sessions aren't required when clients use
 * WebSockets only and skip negotiation). Then, where a WebSocket can't open (a proxy without them), SignalR's
 * negotiation with every transport, which needs sticky sessions at the proxy once there are several pods.
 */
export const hubTransports: readonly IHttpConnectionOptions[] = [
  { transport: HttpTransportType.WebSockets, skipNegotiation: true },
  {},
];

/** Makes the hub connection for one of hubTransports. */
export type HubFactory = (options: IHttpConnectionOptions) => HubConnection;

const buildHub: HubFactory = (options) => new HubConnectionBuilder()
  .withUrl("/hubs/game", options)
  .withHubProtocol(hubTracer.protocol)
  .withAutomaticReconnect()
  .configureLogging(LogLevel.Warning)
  .build();

/**
 * The one realtime connection (src/Pspsps.Server/Hubs/GameHub.cs). With telemetry on, each call is traced (its trace
 * context in the call's headers, which the server's hub span continues), and the connection's life is logged: up,
 * dropped and reconnecting, back, closed, and taken over by another tab.
 */
export class GameConnection {
  private hub: HubConnection;
  // Which of hubTransports the hub uses: the next is tried once if this one can't connect.
  private transport = 0;
  // Set once this tab stops the connection itself (replaced, banned): its close is no surprise.
  private stopping = false;

  constructor(private readonly events: GameEvents, private readonly build: HubFactory = buildHub) {
    this.hub = this.connect(this.transport);
  }

  /** The hub connection for hubTransports[transport], with the game's handlers on it. */
  private connect(transport: number): HubConnection {
    const events = this.events;
    const hub = this.build(hubTransports[transport] ?? {});
    // Only the connection in use speaks for the tab: one that couldn't connect is dropped.
    const current = () => hub === this.hub;
    hub.onreconnecting((error) => appLog.warn("The hub connection dropped; reconnecting", error));
    // Closed for good: stop() (a tab taken over), or a drop the reconnects couldn't get past.
    hub.onclose((error) => error === undefined
      ? appLog.info("The hub connection closed")
      : appLog.warn("The hub connection closed", error));
    hub.on("CatJoined", (cat: CatView) => events.catJoined(cat));
    hub.on("CatLeft", (playerId: string) => events.catLeft(playerId));
    hub.on("CatMoved", (playerId: string, x: number, z: number) => events.catMoved(playerId, x, z));
    hub.on("ChatSaid", (line: ChatView) => events.chatSaid(line));
    hub.on("CatEmoted", (emote: EmoteView) => events.catEmoted(emote));
    hub.on("CatDressed", (outfit: OutfitView) => events.catDressed(outfit));
    hub.on("CatRenamed", (rename: RenameView) => events.catRenamed(rename));
    hub.on("HomeRenamed", (ownerName: string) => events.homeRenamed(ownerName));
    hub.on("Renamed", (displayName: string) => events.renamed(displayName));
    hub.on("FishBit", () => events.fishBit());
    hub.on("FishGotAway", () => events.fishGotAway());
    hub.on("CatFished", (caught: CatchView) => events.catFished(caught));
    hub.onreconnected(() => {
      appLog.info("The hub connection is back");
      events.reconnected();
    });
    hub.on("RoomLost", () => events.roomLost());
    hub.on("WorldLost", (message: string) => events.worldLost(message));
    hub.on("FriendsChanged", (playerId: string) => events.friendsChanged(playerId));
    hub.on("HomeChanged", (home: HomeView) => events.homeChanged(home));
    hub.on("SentOut", (ownerId: string, message: string) => events.sentOut(ownerId, message));
    hub.on("MutesChanged", (mutes: MutedView[]) => events.mutesChanged(mutes));
    hub.on("SessionReplaced", () => {
      appLog.info("Joined from another tab: this one stopped");
      this.stopping = true;
      void hub.stop().then(() => events.replaced());
    });
    hub.on("Banned", (ban: Sanction) => {
      // Shown at once, before anything else the server sends as the connection stops (the cat sent out of its room).
      this.stopping = true;
      events.banned(ban);
      void hub.stop();
    });
    // Dropped: a ban decided on another pod closes the connection, and every try to come back is refused (403). The page
    // asks why at once, rather than after every retry.
    hub.onreconnecting(() => {
      if (current()) {
        events.reconnecting();
      }
    });
    hub.on("Muted", (mute: Sanction | null) => events.muted(mute));
    hub.on("Warned", () => events.warned());
    hub.onclose(() => {
      if (current() && !this.stopping) {
        events.closed();
      }
    });
    return hub;
  }

  /** Stops the connection for good (the player is banned): no retries. */
  stop(): void {
    this.stopping = true;
    void this.hub.stop();
  }

  async start(): Promise<void> {
    if (this.hub.state !== HubConnectionState.Disconnected) {
      return;
    }

    // Each start tries the WebSocket first: a failure that made the last one negotiate (a pod restarting, a dropped
    // network) needn't have been the WebSocket's fault, and negotiation needs sticky sessions with several pods.
    if (this.transport !== 0) {
      this.transport = 0;
      this.hub = this.connect(this.transport);
    }

    try {
      await this.hub.start();
    } catch (error) {
      if (this.stopping || this.transport + 1 >= hubTransports.length) {
        appLog.error("The hub connection failed", error);
        throw error;
      }

      // No WebSocket (a proxy without them, say): SignalR's negotiation, which picks a transport that works.
      appLog.warn("The hub's WebSocket didn't open; negotiating a transport", error);
      this.transport += 1;
      this.hub = this.connect(this.transport);
      try {
        await this.hub.start();
      } catch (fallbackError) {
        appLog.error("The hub connection failed", fallbackError);
        throw fallbackError;
      }
    }
    appLog.info("The hub connection is up");
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

  /** Renames the player's own cat, from their own card; the new name, as the server has it. */
  rename(displayName: string): Promise<string> {
    return this.call<string>("Rename", displayName);
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

  /** An admin's action on another cat (warn, mute, ban, lift); its card as it is after. */
  moderate(playerId: string, request: ModerationRequest): Promise<PlayerCard> {
    return this.call<PlayerCard>(
      "Moderate", playerId, request.action, request.reason || null, request.length ?? null);
  }

  /** An admin bans networks (their keys) the cat, banned, played from lately, for "24h" or "7d"; its card after. */
  banNetworks(playerId: string, networks: string[], length: string): Promise<PlayerCard> {
    return this.call<PlayerCard>("BanNetworks", playerId, networks, length);
  }

  /** An admin lifts the bans on the networks banned with the cat; its card after. */
  liftNetworkBans(playerId: string): Promise<PlayerCard> {
    return this.call<PlayerCard>("LiftNetworkBans", playerId);
  }

  /** The warnings admins left this player that they haven't seen yet, oldest first. */
  getWarnings(): Promise<Warning[]> {
    return this.call<Warning[]>("GetWarnings");
  }

  /** The player closed the warning: it isn't shown again. */
  sawWarning(id: string): Promise<void> {
    return this.call("SawWarning", id);
  }
}

/** The server's message from a refused call, without SignalR's "An unexpected error occurred invoking..." prefix. */
export function messageOf(error: unknown): string {
  const text = error instanceof Error ? error.message : String(error);
  const marker = "HubException: ";
  const at = text.lastIndexOf(marker);
  return at >= 0 ? text.slice(at + marker.length) : text;
}

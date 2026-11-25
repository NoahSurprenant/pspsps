// What the server sends (src/Pspsps.Server/Game/Contracts.cs), as JSON.

import type { CardModeration, Sanction } from "./moderation";

export type FurColor = "Orange" | "Black" | "White" | "Grey" | "Cream" | "Brown" | "Calico" | "Tuxedo";

export interface Player {
  id: string;
  displayName: string;
  furColor: FurColor;
  coins: number;
  isGuest: boolean;
  /** The hat and collar the cat wears (shop item IDs), if any. */
  hat: string | null;
  collar: string | null;
  /** Whether the player is an admin (listed by player ID in the server's settings; missing from an older server). */
  isAdmin?: boolean;
  /** The ban the player is under now, if any: the page shows why, not the game. */
  ban?: Sanction | null;
  /** The mute the player is under now, if any: their chat is off until then. */
  mute?: Sanction | null;
}

export interface CatView {
  playerId: string;
  displayName: string;
  furColor: FurColor;
  x: number;
  z: number;
  /** Sitting or asleep (held until it walks), or null when standing. */
  pose: Pose | null;
  /** The hat and collar it wears (shop item IDs), if any. */
  hat: string | null;
  collar: string | null;
}

/** What a cat wears, sent when it changes (Contracts.cs, OutfitView). */
export interface OutfitView {
  playerId: string;
  furColor: FurColor;
  hat: string | null;
  collar: string | null;
}

/** A cat's new name, sent when it's renamed (Contracts.cs, RenameView). */
export interface RenameView {
  playerId: string;
  displayName: string;
}

/** Where a shop item goes: on the head, round the neck, or the coat itself. */
export type ItemKind = "Hat" | "Collar" | "Fur" | "Furniture";

/** Something in the shop (Contracts.cs, ShopItemView); a fur item has its colour. */
export interface ShopItem {
  id: string;
  name: string;
  kind: ItemKind;
  price: number;
  furColor: FurColor | null;
}

/**
 * The shop as the player sees it (Contracts.cs, ShopView): what's in it, their coins, what they have (and how many) and
 * wear.
 */
export interface Shop {
  items: ShopItem[];
  coins: number;
  /** The item IDs the player has. */
  owned: string[];
  furColor: FurColor;
  hat: string | null;
  collar: string | null;
  /** How many of each item the player has (furniture can be had more than once). */
  counts: Record<string, number>;
  /** How many of one piece of furniture a player can have. */
  maxOfEachFurniture: number;
}

export interface ChatView {
  playerId: string;
  text: string;
  at: string;
}

export interface RoomLink {
  id: string;
  name: string;
}

/** A place on the island; roomId is null while it can't be entered yet. */
export interface MapPlaceView {
  id: string;
  name: string;
  category: string;
  landmark: string;
  x: number;
  z: number;
  blurb: string;
  roomId: string | null;
}

export interface WorldMapView {
  places: MapPlaceView[];
}

export interface RoomSnapshot {
  worldId: string;
  roomId: string;
  roomName: string;
  width: number;
  depth: number;
  cats: CatView[];
  chat: ChatView[];
  rooms: RoomLink[];
  /** The room's pond, if it has one. */
  pond: PondView | null;
  /** The doors out of the room. */
  doors: DoorView[];
  /** The room's decoration, which cats walk through; null from a server older than props (during a deploy). */
  props: PropView[] | null;
  /** The player's home this room is, if it is one (missing from an older server). */
  home?: HomeView | null;
  /**
   * Why the cat is here, when it isn't where it was going: that room was full (or closed to it), and it landed in the
   * next place on its way out ("Town Square is full. You went to Cat Café instead."). Missing from an older server.
   */
  notice?: string | null;
  /** The island's outdoor places. Older pods send the destination list only. */
  worldMap?: WorldMapView | null;
  /** The look an overlay gives the room for now (a named variant of its own look); missing when it has none, or from an older server. */
  look?: string | null;
  /**
   * Set in the open world: every outdoor room, in place. Cat positions in the snapshot and from then on are world metres
   * (east, south), not metres from this room's middle, and the room named above is the one the cat is in now.
   */
  chunks?: ChunkView[] | null;
}

/** One outdoor room of the open world (Contracts.cs, ChunkView): its middle in world metres, and the rest in metres from it. */
export interface ChunkView {
  roomId: string;
  name: string;
  x: number;
  z: number;
  width: number;
  depth: number;
  pond: PondView | null;
  doors: DoorView[];
  props: PropView[];
  look: string | null;
  /** The narrow strips of floor out of its edges, in metres from its middle. */
  paths?: PathView[] | null;
}

/** A narrow strip of floor between two rooms: its middle and size. */
export interface PathView {
  x: number;
  z: number;
  width: number;
  depth: number;
}

/** Who may come into a player's home, besides its owner (the Domain's HomeVisitors). */
export type HomeVisitors = "Friends" | "Anyone" | "Nobody";

/**
 * A player's home (Contracts.cs, HomeView): whose it is, who may visit, the furniture in it, and its version (of two,
 * the newer is kept).
 */
export interface HomeView {
  ownerId: string;
  ownerName: string;
  visitors: HomeVisitors;
  furniture: FurnitureView[];
  version: number;
}

/**
 * A piece of furniture in a home (Contracts.cs, FurnitureView): its ID, the shop item it is (also the kind of prop it's
 * drawn as) and its name, where it stands, its circle, and which way it faces in quarter turns.
 */
export interface FurnitureView {
  id: string;
  itemId: string;
  name: string;
  x: number;
  z: number;
  radius: number;
  quarterTurns: number;
}

/** A door out of a room: a round spot on the floor to walk onto, and the room it leads to. */
export interface DoorView {
  id: string;
  x: number;
  z: number;
  radius: number;
  toRoomId: string;
  toRoomName: string;
  /** The chosen side of the floor. Homes and older pods send no edge. */
  edge?: "north" | "east" | "south" | "west" | null;
}

/**
 * A piece of a room's decoration (Contracts.cs, PropView): what it is, where it stands, the circle round there it is
 * drawn inside, and which way it faces (radians about the vertical; 0 faces the front, where the camera is).
 */
export interface PropView {
  kind: string;
  x: number;
  z: number;
  radius: number;
  facing: number;
}

export interface PondView {
  x: number;
  z: number;
  radius: number;
}

/** How a reel went (src/Pspsps.Server/Game/Contracts.cs, ReelView). */
export interface ReelView {
  outcome: "caught" | "too-early" | "too-late";
  fishId: string | null;
  fishName: string | null;
  coins: number;
  /** The player's coins after a catch; null when nothing was caught. */
  totalCoins: number | null;
}

/** A catch, shown to the room. */
export interface CatchView {
  playerId: string;
  fishId: string;
  fishName: string;
  coins: number;
}

export interface WorldSummary {
  id: string;
  name: string;
  population: number;
  capacity: number;
}

/** The emotes a cat can do (RoomState.Emotes on the server). */
export type Emote = "sit" | "meow" | "wave" | "sleep";

/** The emotes a cat holds until it walks. */
export type Pose = "sit" | "sleep";

export interface EmoteView {
  playerId: string;
  emote: Emote;
  at: string;
}

/** Where one player stands with another, as the first sees it (Domain FriendState). */
export type FriendState = "None" | "RequestSent" | "RequestReceived" | "Friends";

/** Another cat's player card (Contracts.cs, PlayerCardView). */
export interface PlayerCard {
  playerId: string;
  displayName: string;
  furColor: FurColor;
  friendState: FriendState;
  /** The names of the hat and collar the cat wears, if any (not sent with a friends list). */
  hatName?: string | null;
  collarName?: string | null;
  /** Whether the cat's home would let the player in now. */
  canVisitHome?: boolean;
  /** The hat and collar it wears (shop item IDs), for its picture (missing from an older server). */
  hat?: string | null;
  collar?: string | null;
  /** For an admin only: the cat's ban and mute, and whether it is an admin too. */
  moderation?: CardModeration | null;
}

/** Where a friend is (Contracts.cs, FriendPlaceView). */
export interface FriendPlace {
  worldId: string;
  worldName: string;
  roomId: string;
  roomName: string;
  /** Whose home it is, when the friend is in one (which is in no world: worldId and worldName are empty). */
  homeOwnerId?: string | null;
}

/** A friend or a friend request (Contracts.cs, FriendView); a friend in a room has its place, null otherwise. */
export interface Friend extends PlayerCard {
  place: FriendPlace | null;
}

/** A player this one has muted (Contracts.cs, MutedView): their chat is hidden here. */
export interface MutedView {
  playerId: string;
  displayName: string;
  furColor: FurColor;
}

/** Why a player reports another (the Domain's ReportReason). */
export type ReportReason = "MeanChat" | "InappropriateName" | "Spam" | "SomethingElse";

/** A report sent (Contracts.cs, ReportReceipt): whether the cat reported is muted now, and the player's mutes after. */
export interface ReportReceipt {
  muted: boolean;
  mutes: MutedView[];
}

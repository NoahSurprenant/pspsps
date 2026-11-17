// What the server sends (src/Pspsps.Server/Game/Contracts.cs), as JSON.

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
  /** How many of each item the player has (furniture can be had more than once); missing from an older server. */
  counts?: Record<string, number> | null;
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

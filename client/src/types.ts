// What the server sends (src/Pspsps.Server/Game/Contracts.cs), as JSON.

export type FurColor = "Orange" | "Black" | "White" | "Grey" | "Cream" | "Brown" | "Calico" | "Tuxedo";

export interface Player {
  id: string;
  displayName: string;
  furColor: FurColor;
  coins: number;
  isGuest: boolean;
}

export interface CatView {
  playerId: string;
  displayName: string;
  furColor: FurColor;
  x: number;
  z: number;
  /** Sitting or asleep (held until it walks), or null when standing. */
  pose: Pose | null;
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
}

/** Where a friend is (Contracts.cs, FriendPlaceView). */
export interface FriendPlace {
  worldId: string;
  worldName: string;
  roomId: string;
  roomName: string;
}

/** A friend or a friend request (Contracts.cs, FriendView); a friend in a room has its place, null otherwise. */
export interface Friend extends PlayerCard {
  place: FriendPlace | null;
}

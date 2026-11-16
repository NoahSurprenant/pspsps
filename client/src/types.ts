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
}

export interface WorldSummary {
  id: string;
  name: string;
  population: number;
  capacity: number;
}

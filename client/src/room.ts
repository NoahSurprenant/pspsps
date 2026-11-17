import type { CatView, ChatView, EmoteView, RoomSnapshot } from "./types";

/**
 * The room as this browser knows it. Every change is idempotent: the server may send a join for a cat already in the
 * snapshot (the connection joins the room's group before the snapshot is taken).
 */
export class Room {
  readonly cats = new Map<string, CatView>();
  readonly chat: ChatView[] = [];
  /** Each cat's latest emote, while it is still in the room. */
  readonly emotes = new Map<string, EmoteView>();

  constructor(readonly snapshot: RoomSnapshot) {
    for (const cat of snapshot.cats) {
      this.cats.set(cat.playerId, cat);
    }
    this.chat.push(...snapshot.chat);
  }

  join(cat: CatView): void {
    this.cats.set(cat.playerId, cat);
  }

  leave(playerId: string): void {
    this.cats.delete(playerId);
    this.emotes.delete(playerId);
  }

  move(playerId: string, x: number, z: number): CatView | undefined {
    const cat = this.cats.get(playerId);
    if (!cat) {
      return undefined;
    }
    const moved = { ...cat, x, z };
    this.cats.set(playerId, moved);
    return moved;
  }

  say(line: ChatView): void {
    this.chat.push(line);
    if (this.chat.length > Room.chatHistory) {
      this.chat.splice(0, this.chat.length - Room.chatHistory);
    }
  }

  emote(emote: EmoteView): void {
    if (this.cats.has(emote.playerId)) {
      this.emotes.set(emote.playerId, emote);
    }
  }

  nameOf(playerId: string): string {
    return this.cats.get(playerId)?.displayName ?? "A cat";
  }

  static readonly chatHistory = 50;
}

import { isPose } from "./emotes";
import { newer, propsOf } from "./home";
import type { CatView, ChatView, EmoteView, HomeView, OutfitView, RoomSnapshot } from "./types";

/**
 * The room as this browser knows it. Every change is idempotent: the server may send a join for a cat already in the
 * snapshot (the connection joins the room's group before the snapshot is taken).
 */
export class Room {
  readonly cats = new Map<string, CatView>();
  readonly chat: ChatView[] = [];
  /** Each cat's latest emote, until it walks or leaves. Whether it is sitting or asleep is on the cat (`pose`). */
  readonly emotes = new Map<string, EmoteView>();

  /** The room as it arrived, and a home's furniture as it has changed since. */
  snapshot: RoomSnapshot;

  /** @param hides Whose chat the player has muted: their lines, the room's history among them, are never kept here. */
  constructor(snapshot: RoomSnapshot, private readonly hides: (playerId: string) => boolean = () => false) {
    // Not even in the snapshot as it arrived: nothing here keeps what a muted cat said.
    this.snapshot = { ...snapshot, chat: snapshot.chat.filter((line) => !hides(line.playerId)) };
    for (const cat of snapshot.cats) {
      this.cats.set(cat.playerId, cat);
    }
    this.chat.push(...this.snapshot.chat);
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
    // A cat that walks gets up, as on the server.
    this.emotes.delete(playerId);
    const moved = { ...cat, x, z, pose: null };
    this.cats.set(playerId, moved);
    return moved;
  }

  /**
   * Adds a line said here; false for one it already has (the snapshot may hold a line also sent live) or one from a cat
   * that isn't in this room (a late line from the room just left), or one from a cat the player has muted.
   */
  say(line: ChatView): boolean {
    const known = this.chat.some((l) => l.playerId === line.playerId && l.at === line.at && l.text === line.text);
    if (known || !this.cats.has(line.playerId) || this.hides(line.playerId)) {
      return false;
    }
    this.chat.push(line);
    if (this.chat.length > Room.chatHistory) {
      this.chat.splice(0, this.chat.length - Room.chatHistory);
    }
    return true;
  }

  /** The player muted this cat: what it said is no longer kept. */
  hush(playerId: string): void {
    const kept = this.chat.filter((line) => line.playerId !== playerId);
    this.chat.splice(0, this.chat.length, ...kept);
    this.snapshot = { ...this.snapshot, chat: this.snapshot.chat.filter((line) => line.playerId !== playerId) };
  }

  emote(emote: EmoteView): void {
    const cat = this.cats.get(emote.playerId);
    if (!cat) {
      return;
    }
    this.emotes.set(emote.playerId, emote);
    if (isPose(emote.emote)) {
      this.cats.set(emote.playerId, { ...cat, pose: emote.emote });
    }
  }

  /** The cat put on or took off something, or changed its fur; false for a cat that isn't here. */
  dress(outfit: OutfitView): boolean {
    const cat = this.cats.get(outfit.playerId);
    if (!cat) {
      return false;
    }
    this.cats.set(outfit.playerId, { ...cat, furColor: outfit.furColor, hat: outfit.hat, collar: outfit.collar });
    return true;
  }

  /**
   * The home's owner changed it: its furniture (the room's props) and who may visit are as <home> has them from now on.
   * False when this room isn't that home, or already has a newer change.
   */
  furnish(home: HomeView): boolean {
    const current = this.snapshot.home;
    if (!current || current.ownerId !== home.ownerId || newer(current, home) !== home) {
      return false;
    }
    this.snapshot = { ...this.snapshot, home, props: propsOf(home) };
    return true;
  }

  nameOf(playerId: string): string {
    return this.cats.get(playerId)?.displayName ?? "A cat";
  }

  static readonly chatHistory = 50;
}

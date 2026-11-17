import { isPose } from "./emotes";
import type { Emote, Pose } from "./types";

/**
 * What one cat looks like apart from where it stands: its held pose, the bubble over it, a wave and a meow. Plain state
 * with times passed in; the scene draws it every frame.
 */
export class CatLook {
  static readonly bubbleMilliseconds = 6_000;
  static readonly waveMilliseconds = 1_200;
  static readonly meowMilliseconds = 2_500;

  pose: Pose | undefined;
  private said: { text: string; until: number } | undefined;
  private waveUntil = -Infinity;
  // Apart from the chat line, so a meow never cuts one short.
  private meowUntil = -Infinity;
  /** How many meows so far: the cat's meow animation starts again on each. */
  meows = 0;

  constructor(pose?: Pose | null) {
    this.pose = pose ?? undefined;
  }

  /** The cat was sent somewhere new: it gets up. A walk already under way doesn't end a pose taken during it. */
  walk(): void {
    this.pose = undefined;
  }

  emote(emote: Emote, now: number): void {
    if (isPose(emote)) {
      this.pose = emote;
    } else if (emote === "meow") {
      this.meowUntil = now + CatLook.meowMilliseconds;
      this.meows++;
    } else {
      this.waveUntil = now + CatLook.waveMilliseconds;
    }
  }

  say(text: string, now: number): void {
    this.said = { text, until: now + CatLook.bubbleMilliseconds };
  }

  /**
   * The bubble at <now>: the latest chat line while it lasts, with "meow!" under it for a moment after a meow; without
   * either, "z z z" while asleep.
   */
  bubbleAt(now: number): string | undefined {
    const lines = [
      ...(this.said && now <= this.said.until ? [this.said.text] : []),
      ...(now <= this.meowUntil ? ["meow!"] : []),
    ];
    if (lines.length > 0) {
      return lines.join("\n");
    }
    return this.pose === "sleep" ? "z z z" : undefined;
  }

  wavingAt(now: number): boolean {
    return now <= this.waveUntil;
  }

  meowingAt(now: number): boolean {
    return now <= this.meowUntil;
  }
}

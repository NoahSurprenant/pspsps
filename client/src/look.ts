import type { Emote, Pose } from "./types";

/**
 * What one cat looks like apart from where it stands: its held pose, the bubble over it and a raised paw. Plain state
 * with times passed in; the scene draws it every frame.
 */
export class CatLook {
  static readonly bubbleMilliseconds = 6_000;
  static readonly waveMilliseconds = 1_200;

  pose: Pose | undefined;
  private said: { text: string; until: number } | undefined;
  private waveUntil = -Infinity;

  constructor(pose?: Pose | null) {
    this.pose = pose ?? undefined;
  }

  /** The cat was sent somewhere new: it gets up. A walk already under way doesn't end a pose taken during it. */
  walk(): void {
    this.pose = undefined;
  }

  emote(emote: Emote, now: number): void {
    switch (emote) {
      case "sit":
      case "sleep":
        this.pose = emote;
        break;
      case "meow":
        this.say("meow!", now);
        break;
      case "wave":
        this.waveUntil = now + CatLook.waveMilliseconds;
        break;
    }
  }

  say(text: string, now: number): void {
    this.said = { text, until: now + CatLook.bubbleMilliseconds };
  }

  /** The bubble at <now>: the latest line while it lasts, then "z z z" while asleep. */
  bubbleAt(now: number): string | undefined {
    if (this.said && now <= this.said.until) {
      return this.said.text;
    }
    return this.pose === "sleep" ? "z z z" : undefined;
  }

  wavingAt(now: number): boolean {
    return now <= this.waveUntil;
  }
}

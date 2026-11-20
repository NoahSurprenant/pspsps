import { isTyping } from "./emotes";

const directions = new Map([
  ["KeyW", { x: 0, z: 1 }],
  ["KeyA", { x: -1, z: 0 }],
  ["KeyS", { x: 0, z: -1 }],
  ["KeyD", { x: 1, z: 0 }],
]);

/** Sends short steps while WASD is held, with one move in flight and no dependency on OS key repeat. */
export class WalkKeys {
  private readonly held = new Set<string>();
  private timer: number | undefined;
  private sending = false;

  constructor(
    private readonly canWalk: () => boolean,
    private readonly move: (dx: number, dz: number) => Promise<void>,
  ) {
    window.addEventListener("keydown", (event) => {
      if (!directions.has(event.code) || event.altKey || event.ctrlKey || event.metaKey || !this.available()) {
        return;
      }
      event.preventDefault();
      if (event.repeat || this.held.has(event.code)) {
        return;
      }
      this.held.add(event.code);
      this.timer ??= window.setInterval(() => void this.step(), 120);
      void this.step();
    });
    window.addEventListener("keyup", (event) => {
      if (this.held.delete(event.code) && this.held.size === 0) {
        this.stop();
      }
    });
    window.addEventListener("blur", () => this.stop());
    window.addEventListener("pointerdown", () => this.stop(), { capture: true });
    document.addEventListener("visibilitychange", () => {
      if (document.hidden) {
        this.stop();
      }
    });
  }

  stop(): void {
    this.held.clear();
    window.clearInterval(this.timer);
    this.timer = undefined;
  }

  private available(): boolean {
    return this.canWalk() && !isTyping(document.activeElement);
  }

  private async step(): Promise<void> {
    if (!this.available()) {
      this.stop();
      return;
    }
    const vector = { x: 0, z: 0 };
    for (const [key, dir] of directions) {
      if (this.held.has(key)) {
        vector.x += dir.x;
        vector.z += dir.z;
      }
    }
    if (this.sending || (vector.x === 0 && vector.z === 0)) {
      return;
    }
    this.sending = true;
    try {
      await this.move(vector.x, vector.z);
    } finally {
      this.sending = false;
    }
  }
}

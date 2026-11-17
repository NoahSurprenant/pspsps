/**
 * Room events that arrive while the next room loads (between asking to enter and its snapshot showing), held to replay
 * onto it in order. Some may already be in the snapshot: the room's updates are idempotent, so replaying them is
 * harmless, while dropping them would lose whatever happened just after the snapshot was taken.
 */
export class PendingEvents<T> {
  private held: T[] | undefined;

  /** A room starts loading: hold everything until it shows. */
  start(): void {
    this.held = [];
  }

  /** Keeps <event> while a room loads; false when none is, and the event applies now. */
  hold(event: T): boolean {
    this.held?.push(event);
    return this.held !== undefined;
  }

  /** The room showed: the held events, oldest first. Stops holding. */
  finish(): T[] {
    const held = this.held ?? [];
    this.held = undefined;
    return held;
  }

  /** Entering failed: drop what was held. */
  cancel(): void {
    this.held = undefined;
  }
}

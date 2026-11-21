/** How often a page's room is drawn at most: 30 a second looks the same for this game and halves the work of 60. */
export const MAX_FPS = 30;

/** Says whether a frame arriving at <now> (ms) should be drawn, remembering it when so. */
export function frameLimiter(maxFps = MAX_FPS): (now: number) => boolean {
  const gap = 1000 / maxFps;
  let last = -Infinity;
  return (now) => {
    // A little early is fine: display frames land a hair before the exact gap, and would otherwise be skipped for 60.
    if (now - last < gap * 0.9) {
      return false;
    }
    last = now;
    return true;
  };
}

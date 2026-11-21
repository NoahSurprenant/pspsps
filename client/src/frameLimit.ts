/** What the player chose for how often the room is drawn; "auto" is every frame with a mouse, 30 a second on a touch screen. */
export type FrameRate = "auto" | "30" | "60" | "unlimited";

export const FRAME_RATES: readonly { value: FrameRate; label: string }[] = [
  { value: "auto", label: "Automatic" },
  { value: "30", label: "30 a second" },
  { value: "60", label: "60 a second" },
  { value: "unlimited", label: "As fast as the screen goes" },
];

export const FRAME_RATE_KEY = "pspsps.frameRate";

/** The saved choice, "auto" when there is none or it isn't one of ours. */
export function savedFrameRate(storage: Pick<Storage, "getItem">): FrameRate {
  const saved = storage.getItem(FRAME_RATE_KEY);
  return FRAME_RATES.some((rate) => rate.value === saved) ? (saved as FrameRate) : "auto";
}

/** Frames a second to draw at most, or undefined for no limit. */
export function maxFps(rate: FrameRate, touch: boolean): number | undefined {
  if (rate === "auto") {
    return touch ? 30 : undefined;
  }
  return rate === "unlimited" ? undefined : Number(rate);
}

/** Says whether a frame arriving at <now> (ms) should be drawn, remembering it when so. <limit> is read every frame. */
export function frameLimiter(limit: () => number | undefined): (now: number) => boolean {
  let last = -Infinity;
  return (now) => {
    const fps = limit();
    // A little early is fine: display frames land a hair before the exact gap, and would otherwise be skipped for 60.
    if (fps !== undefined && now - last < (1000 / fps) * 0.9) {
      return false;
    }
    last = now;
    return true;
  };
}

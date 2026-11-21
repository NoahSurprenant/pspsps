import { describe, expect, it } from "vitest";
import { FRAME_RATE_KEY, frameClock, frameLimiter, maxFps, savedFrameRate } from "./frameLimit";

const drawn = (fps: number | undefined, displayHz: number) => {
  const draw = frameLimiter(() => fps);
  return Array.from({ length: displayHz }, (_, i) => draw(i * (1000 / displayHz))).filter(Boolean).length;
};

describe("frameLimiter", () => {
  it("draws every other frame of a 60 a second display at 30", () => {
    expect(drawn(30, 60)).toBe(30);
  });

  it("draws every frame of a display slower than the limit", () => {
    expect(drawn(30, 20)).toBe(20);
  });

  it("draws every frame with no limit", () => {
    expect(drawn(undefined, 144)).toBe(144);
  });

  it("follows a limit that changes", () => {
    let fps: number | undefined = undefined;
    const draw = frameLimiter(() => fps);
    expect(draw(0) && draw(16)).toBe(true);
    fps = 10;
    expect(draw(32)).toBe(false);
    expect(draw(120)).toBe(true);
  });
});

describe("maxFps", () => {
  it("is automatic: unlimited with a mouse, 30 on a touch screen", () => {
    expect(maxFps("auto", false)).toBeUndefined();
    expect(maxFps("auto", true)).toBe(30);
  });

  it("is what was chosen otherwise, on either kind of screen", () => {
    expect(maxFps("60", true)).toBe(60);
    expect(maxFps("30", false)).toBe(30);
    expect(maxFps("unlimited", true)).toBeUndefined();
  });
});

describe("savedFrameRate", () => {
  const storage = (value: string | null) => ({ getItem: (key: string) => (key === FRAME_RATE_KEY ? value : null) });

  it("is the saved choice", () => {
    expect(savedFrameRate(storage("60"))).toBe("60");
  });

  it("is automatic with none saved or one it doesn't know", () => {
    expect(savedFrameRate(storage(null))).toBe("auto");
    expect(savedFrameRate(storage("999"))).toBe("auto");
  });
});

describe("frameClock", () => {
  it("walks the same distance a second whatever the frame rate", () => {
    const metresIn = (fps: number | undefined) => {
      const draw = frameLimiter(() => fps);
      const tick = frameClock();
      let metres = 0;
      for (let i = 0; i < 60; i++) {
        const now = i * (1000 / 60);
        if (draw(now)) {
          metres += 4 * tick(now);
        }
      }
      return metres;
    };
    expect(metresIn(30)).toBeCloseTo(metresIn(undefined), 0);
  });

  it("gives no time on the first frame and caps a long pause", () => {
    const tick = frameClock();
    expect(tick(5000)).toBe(0);
    expect(tick(60000)).toBe(0.25);
  });
});

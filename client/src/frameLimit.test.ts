import { describe, expect, it } from "vitest";
import { frameLimiter } from "./frameLimit";

describe("frameLimiter", () => {
  it("draws the first frame", () => {
    expect(frameLimiter(30)(0)).toBe(true);
  });

  it("draws every other frame of a 60 a second display at 30", () => {
    const draw = frameLimiter(30);
    const drawn = Array.from({ length: 60 }, (_, i) => draw(i * (1000 / 60))).filter(Boolean).length;
    expect(drawn).toBe(30);
  });

  it("draws every frame of a display slower than the limit", () => {
    const draw = frameLimiter(30);
    expect(Array.from({ length: 10 }, (_, i) => draw(i * 50)).every(Boolean)).toBe(true);
  });
});

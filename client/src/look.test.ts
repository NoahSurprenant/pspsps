import { describe, expect, it } from "vitest";
import { CatLook } from "./look";

const t0 = 1_000_000;

describe("CatLook", () => {
  it("starts in the pose the room says the cat is in", () => {
    expect(new CatLook("sleep").pose).toBe("sleep");
    expect(new CatLook(null).pose).toBeUndefined();
  });

  it("holds sit and sleep; meow and wave don't change the pose", () => {
    const look = new CatLook();

    look.emote("sit", t0);
    expect(look.pose).toBe("sit");
    look.emote("meow", t0);
    look.emote("wave", t0);
    expect(look.pose).toBe("sit");
    look.emote("sleep", t0);
    expect(look.pose).toBe("sleep");
  });

  // The cat may still be walking on screen when the emote arrives: only a new walk ends the pose, not time passing.
  it("keeps a pose until the cat is sent somewhere new", () => {
    const look = new CatLook();
    look.emote("sit", t0);

    expect(look.bubbleAt(t0 + 60_000)).toBeUndefined();
    expect(look.pose).toBe("sit");

    look.walk();
    expect(look.pose).toBeUndefined();
  });

  it("snores while asleep, and the snore comes back after a chat line or a meow", () => {
    const look = new CatLook();
    look.emote("sleep", t0);
    expect(look.bubbleAt(t0)).toBe("z z z");

    look.say("five more minutes", t0 + 1_000);
    expect(look.bubbleAt(t0 + 2_000)).toBe("five more minutes");
    expect(look.bubbleAt(t0 + 1_000 + CatLook.bubbleMilliseconds + 1)).toBe("z z z");

    look.emote("meow", t0 + 10_000);
    expect(look.bubbleAt(t0 + 10_001)).toBe("meow!");
    expect(look.bubbleAt(t0 + 10_000 + CatLook.meowMilliseconds + 1)).toBe("z z z");

    look.walk();
    expect(look.bubbleAt(t0 + 60_000)).toBeUndefined();
  });

  it("doesn't cut a chat line short with a meow: both show, then the line alone", () => {
    const look = new CatLook();
    look.say("come here", t0);

    look.emote("meow", t0 + 1_000);

    expect(look.bubbleAt(t0 + 1_001)).toBe("come here\nmeow!");
    expect(look.bubbleAt(t0 + 1_000 + CatLook.meowMilliseconds + 1)).toBe("come here");
    expect(look.bubbleAt(t0 + CatLook.bubbleMilliseconds + 1)).toBeUndefined();
  });

  it("shows a meow on its own for a moment", () => {
    const look = new CatLook();
    look.emote("meow", t0);

    expect(look.bubbleAt(t0 + 1)).toBe("meow!");
    expect(look.bubbleAt(t0 + CatLook.meowMilliseconds + 1)).toBeUndefined();
  });

  it("shows a chat line for a while when awake", () => {
    const look = new CatLook();
    look.say("pspsps", t0);

    expect(look.bubbleAt(t0 + 1)).toBe("pspsps");
    expect(look.bubbleAt(t0 + CatLook.bubbleMilliseconds + 1)).toBeUndefined();
  });

  it("raises a paw for a moment, in any pose", () => {
    const look = new CatLook("sleep");
    expect(look.wavingAt(t0)).toBe(false);

    look.emote("wave", t0);

    expect(look.wavingAt(t0 + 100)).toBe(true);
    expect(look.wavingAt(t0 + CatLook.waveMilliseconds + 1)).toBe(false);
    expect(look.pose).toBe("sleep");
  });

  it("meows for a moment, and counts each meow so the animation starts again", () => {
    const look = new CatLook();
    expect(look.meowingAt(t0)).toBe(false);

    look.emote("meow", t0);
    expect(look.meowingAt(t0 + 100)).toBe(true);
    expect(look.meows).toBe(1);
    look.emote("meow", t0 + 200);
    expect(look.meows).toBe(2);

    expect(look.meowingAt(t0 + 200 + CatLook.meowMilliseconds + 1)).toBe(false);
  });
});

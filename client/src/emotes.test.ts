import { describe, expect, it } from "vitest";
import { emoteBar, emoteForKey, isPose, isTyping, poseScales } from "./emotes";

const onPage = { tagName: "BODY", isContentEditable: false } as unknown as EventTarget;
const chatBox = { tagName: "INPUT", isContentEditable: false } as unknown as EventTarget;

function press(key: string, more: Partial<Parameters<typeof emoteForKey>[0]> = {}) {
  return emoteForKey({ key, altKey: false, ctrlKey: false, metaKey: false, repeat: false, target: onPage, ...more });
}

describe("emoteForKey", () => {
  it("maps 1-4 to sit, meow, wave and sleep", () => {
    expect(["1", "2", "3", "4"].map((key) => press(key))).toEqual(["sit", "meow", "wave", "sleep"]);
  });

  it("ignores other keys, modifiers and held keys", () => {
    expect(press("5")).toBeUndefined();
    expect(press("a")).toBeUndefined();
    expect(press("1", { ctrlKey: true })).toBeUndefined();
    expect(press("1", { altKey: true })).toBeUndefined();
    expect(press("1", { metaKey: true })).toBeUndefined();
    expect(press("1", { repeat: true })).toBeUndefined();
  });

  it("leaves digits typed in the chat box alone", () => {
    expect(press("1", { target: chatBox })).toBeUndefined();
  });
});

describe("isTyping", () => {
  it("is true in text fields and editable elements only", () => {
    expect(isTyping(chatBox)).toBe(true);
    expect(isTyping({ tagName: "TEXTAREA" } as unknown as EventTarget)).toBe(true);
    expect(isTyping({ tagName: "DIV", isContentEditable: true } as unknown as EventTarget)).toBe(true);
    expect(isTyping({ tagName: "BUTTON", isContentEditable: false } as unknown as EventTarget)).toBe(false);
    expect(isTyping(onPage)).toBe(false);
    expect(isTyping(null)).toBe(false);
  });
});

describe("poseScales", () => {
  it("squashes a sitting cat, and a sleeping one flatter", () => {
    expect(poseScales.sit[1]).toBeLessThan(1);
    expect(poseScales.sleep[1]).toBeLessThan(poseScales.sit[1]);
  });
});

describe("isPose", () => {
  it("is true for the emotes a cat holds, and each has a scale", () => {
    expect(emoteBar.map((entry) => entry.emote).filter(isPose)).toEqual(["sit", "sleep"]);
    expect(Object.keys(poseScales).sort()).toEqual(["sit", "sleep"]);
  });
});

describe("emoteBar", () => {
  it("offers every emote once, each with its own key", () => {
    expect(emoteBar.map((entry) => entry.emote)).toEqual(["sit", "meow", "wave", "sleep"]);
    expect(new Set(emoteBar.map((entry) => entry.key)).size).toBe(emoteBar.length);
  });
});

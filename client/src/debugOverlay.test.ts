import { afterEach, describe, expect, it, vi } from "vitest";
import { DEBUG_KEY, DebugOverlay, debugWanted, type DebugBox } from "./debugOverlay";

const store = (initial?: string) => {
  const data = new Map<string, string>(initial ? [[DEBUG_KEY, initial]] : []);
  return { getItem: (k: string) => data.get(k) ?? null, setItem: (k: string, v: string) => void data.set(k, v) };
};

const page = () => {
  const listeners: ((event: { code: string; altKey: boolean; ctrlKey: boolean; metaKey: boolean; preventDefault(): void }) => void)[] = [];
  const box: DebugBox = { hidden: false, textContent: "" };
  return {
    box,
    keys: { addEventListener: (_: "keydown", listener: (typeof listeners)[number]) => void listeners.push(listener) },
    press: (code: string) => listeners.forEach((l) => l({ code, altKey: false, ctrlKey: false, metaKey: false, preventDefault: () => {} })),
  };
};

describe("debugWanted", () => {
  it("follows the address, else what was left on last time", () => {
    expect(debugWanted("?debug", store())).toBe(true);
    expect(debugWanted("?debug=0", store("1"))).toBe(false);
    expect(debugWanted("", store("1"))).toBe(true);
    expect(debugWanted("", store())).toBe(false);
  });
});

describe("DebugOverlay", () => {
  afterEach(() => vi.useRealTimers());

  it("shows the room and the pinged round trip once F3 is pressed, and hides on the next press", async () => {
    vi.useFakeTimers();
    const ping = vi.fn(() => Promise.resolve());
    const { box, keys, press } = page();
    const overlay = new DebugOverlay(() => ({ roomName: "Cat Café", roomId: "cafe", cats: 1, connection: "Connected" }), ping, store(), "", box, keys);
    expect(box.hidden).toBe(true);

    press("F3");
    await vi.advanceTimersByTimeAsync(600);

    expect(overlay.shown).toBe(true);
    expect(box.textContent).toContain("Room: Cat Café (cafe)");
    expect(ping).toHaveBeenCalled();
    press("F3");
    expect(box.hidden).toBe(true);
    overlay.hide();
  });

  it("notes a ping that gets no reply", async () => {
    vi.useFakeTimers();
    const { box, keys } = page();
    const overlay = new DebugOverlay(() => undefined, () => Promise.reject(new Error("down")), store(), "?debug", box, keys);
    await vi.advanceTimersByTimeAsync(100);

    expect(overlay.stats.events.map((e) => e.text)).toEqual(["no reply to the ping"]);
    overlay.hide();
  });
});

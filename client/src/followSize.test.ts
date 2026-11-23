import { afterEach, describe, expect, it, vi } from "vitest";
import { followSize } from "./followSize";

let observers: FakeObserver[] = [];

class FakeObserver {
  observed: unknown[] = [];
  disconnected = false;
  constructor(readonly callback: () => void) {
    observers.push(this);
  }
  observe(target: unknown) {
    this.observed.push(target);
  }
  disconnect() {
    this.disconnected = true;
  }
}

describe("following the size of the map picture", () => {
  afterEach(() => {
    observers = [];
    vi.unstubAllGlobals();
  });

  it("sizes the picture once now and again whenever its box changes, until stopped", () => {
    vi.stubGlobal("ResizeObserver", FakeObserver);
    const canvas = {} as HTMLCanvasElement;
    const resize = vi.fn();

    const stop = followSize(canvas, resize);
    expect(resize).toHaveBeenCalledTimes(1);
    expect(observers[0]?.observed).toEqual([canvas]);

    observers[0]?.callback();
    expect(resize).toHaveBeenCalledTimes(2);

    stop();
    expect(observers[0]?.disconnected).toBe(true);
  });
});

import { afterEach, describe, expect, it, vi } from "vitest";
import { followSize } from "./followSize";

class FakeObserver {
  static all: FakeObserver[] = [];
  observed: unknown[] = [];
  disconnected = false;
  constructor(readonly callback: () => void) {
    FakeObserver.all.push(this);
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
    FakeObserver.all = [];
    vi.unstubAllGlobals();
  });

  it("sizes the picture once now and again whenever its box changes, until stopped", () => {
    vi.stubGlobal("ResizeObserver", FakeObserver);
    const canvas = {} as HTMLCanvasElement;
    const resize = vi.fn();

    const stop = followSize(canvas, resize);
    expect(resize).toHaveBeenCalledTimes(1);
    expect(FakeObserver.all[0]?.observed).toEqual([canvas]);

    FakeObserver.all[0]?.callback();
    expect(resize).toHaveBeenCalledTimes(2);

    stop();
    expect(FakeObserver.all[0]?.disconnected).toBe(true);
  });
});

import type { AssetContainer } from "@babylonjs/core/assetContainer";
import { NullEngine } from "@babylonjs/core";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { RoomScene } from "./scene";
import type { RoomSnapshot } from "./types";

const room: RoomSnapshot = {
  worldId: "tabby", roomId: "town-square", roomName: "Town Square", width: 20, depth: 12, cats: [], chat: [], rooms: [],
  pond: null,
};

/** A promise and the hands to settle it. */
function deferred<T>(): { promise: Promise<T>; resolve(value: T): void; reject(error: unknown): void } {
  let resolve!: (value: T) => void;
  let reject!: (error: unknown) => void;
  const promise = new Promise<T>((yes, no) => {
    resolve = yes;
    reject = no;
  });
  return { promise, resolve, reject };
}

/** A model loader whose first room gets <first>, and every later one a model still loading. */
function firstThen(first: Promise<AssetContainer>): () => Promise<AssetContainer> {
  let calls = 0;
  return () => (calls++ === 0 ? first : new Promise<AssetContainer>(() => {}));
}

/**
 * A canvas for the room's GUI layer (a dynamic texture), which Node lacks: its 2D context draws nothing, and measures
 * all text as nothing.
 */
class NoCanvas {
  width = 1;
  height = 1;

  getContext(): unknown {
    return new Proxy({}, {
      get: (target: Record<string | symbol, unknown>, key) =>
        key in target ? target[key] : key === "measureText" ? () => ({ width: 0 }) : () => undefined,
    });
  }
}

describe("RoomScene", () => {
  beforeEach(() => vi.stubGlobal("OffscreenCanvas", NoCanvas));
  afterEach(() => vi.unstubAllGlobals());

  // The page draws from the moment it loads, before any room is showing (sign-in, world picker). A frame that throws
  // stops Babylon's render loop for good, so no room would ever draw after it.
  it("draws a frame before any room is showing", () => {
    const scene = new RoomScene(new NullEngine());

    expect(() => scene.renderFrame()).not.toThrow();
  });

  // A door or another world picked while the first room's cat model loads: the old room's model arrives after its
  // scene is gone, and nothing else would ever free it.
  it("frees a cat model that finishes loading after its room was left", async () => {
    const first = deferred<AssetContainer>();
    const scene = new RoomScene(new NullEngine(), firstThen(first.promise));
    scene.show(room);
    scene.show({ ...room, roomId: "cafe" });

    const model = { dispose: vi.fn() } as unknown as AssetContainer;
    first.resolve(model);
    await vi.waitFor(() => expect(model.dispose).toHaveBeenCalled());
  });

  it("doesn't call a model that failed after its room was left an error", async () => {
    const errors = vi.spyOn(console, "error").mockImplementation(() => {});
    try {
      const first = deferred<AssetContainer>();
      const scene = new RoomScene(new NullEngine(), firstThen(first.promise));
      scene.show(room);
      scene.show({ ...room, roomId: "cafe" });

      first.reject(new Error("the scene was disposed"));
      await new Promise((resolve) => setTimeout(resolve, 10));

      expect(errors).not.toHaveBeenCalled();
    } finally {
      errors.mockRestore();
    }
  });
});

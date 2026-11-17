import type { AssetContainer } from "@babylonjs/core/assetContainer";
import { NullEngine, Scene, TransformNode } from "@babylonjs/core";
import { CreateBox } from "@babylonjs/core/Meshes/Builders/boxBuilder";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { loadCatModel } from "./cat";
import catGlb from "./models/cat.glb?inline";
import { catPlayerIdOf, RoomScene } from "./scene";
import type { RoomSnapshot } from "./types";

const room: RoomSnapshot = {
  worldId: "tabby", roomId: "town-square", roomName: "Town Square", width: 20, depth: 12, cats: [], chat: [], rooms: [],
  pond: null, doors: [],
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

/** The real cat model's bytes, from the data URL Vite makes of it. */
function modelBytes(): Uint8Array {
  return Uint8Array.from(atob(catGlb.slice(catGlb.indexOf(",") + 1)), (c) => c.charCodeAt(0));
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

  // Another cat's card opens with a tap on any part of it, as soon as its model is drawn, also for cats placed while the
  // model was still loading; the player's own cat never takes the pointer, so a tap just behind it still walks or fishes.
  it("lets other cats be tapped once their model loads, and never the player's own", async () => {
    const loaded = deferred<true>();
    const view = new RoomScene(new NullEngine(), async (scene) => {
      await loaded.promise;
      return loadCatModel(scene, modelBytes());
    });
    view.playerId = "tom";
    view.show({ ...room, cats: [{ playerId: "tom", displayName: "Tom", furColor: "Grey", x: 0, z: 0, pose: null }] });
    view.place({ playerId: "felix", displayName: "Felix", furColor: "Black", x: 2, z: 0, pose: null });

    loaded.resolve(true);
    await vi.waitFor(() => expect(view.animationOf("felix")).toBeDefined());
    view.place({ playerId: "garfield", displayName: "Garfield", furColor: "Orange", x: -2, z: 0, pose: null });
    await vi.waitFor(() => expect(view.animationOf("garfield")).toBeDefined());
    expect(view.animationOf("tom")).toBeDefined();

    const meshes = (view as unknown as { scene: Scene }).scene.meshes;
    const pickable = (id: string) => [...new Set(meshes
      .filter((mesh) => catPlayerIdOf(mesh) === id && !mesh.name.startsWith("anchor-"))
      .map((mesh) => mesh.isPickable))];
    expect(pickable("felix")).toEqual([true]);
    expect(pickable("garfield")).toEqual([true]);
    expect(pickable("tom")).toEqual([false]);
    expect(meshes.filter((mesh) => mesh.name.startsWith("anchor-")).every((anchor) => !anchor.isPickable)).toBe(true);
  });
});

describe("catPlayerIdOf", () => {
  it("finds the cat a picked mesh is part of, however deep, and nothing for other meshes", () => {
    const scene = new Scene(new NullEngine());
    const root = new TransformNode("cat", scene);
    root.metadata = { catPlayerId: "felix" };
    const body = new TransformNode("body", scene);
    body.parent = root;
    const ear = CreateBox("ear", {}, scene);
    ear.parent = body;

    expect(catPlayerIdOf(ear)).toBe("felix");
    expect(catPlayerIdOf(CreateBox("floor", {}, scene))).toBeUndefined();
    expect(catPlayerIdOf(null)).toBeUndefined();
  });
});

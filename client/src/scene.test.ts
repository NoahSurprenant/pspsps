import type { AssetContainer } from "@babylonjs/core/assetContainer";
import { NullEngine, Scene, StandardMaterial, TransformNode, Vector3, type PickingInfo } from "@babylonjs/core";
import { CreateBox } from "@babylonjs/core/Meshes/Builders/boxBuilder";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { loadCatModel } from "./cat";
import seed from "../../src/Pspsps.Application/Rooms/RoomLooks.json?raw";
import { lookOf, setLooks, type LooksData } from "./looks";
import { catPlayerIdOf, RoomScene } from "./scene";
import { modelBytes } from "./testCatModel";
import type { RoomSnapshot } from "./types";

const room: RoomSnapshot = {
  worldId: "tabby", roomId: "town-square", roomName: "Town Square", width: 20, depth: 12, cats: [], chat: [], rooms: [],
  pond: null, doors: [], props: [],
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
    const fakeMember = (key: string | symbol) => (key === "measureText" ? () => ({ width: 0 }) : () => undefined);
    return new Proxy({}, {
      get: (target: Record<string | symbol, unknown>, key) =>
        key in target ? target[key] : fakeMember(key),
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

  // A cat's card opens with a tap on any part of it, as soon as its model is drawn, also for cats placed while the model
  // was still loading; the player's own too (its own card), which the tap puts after doors and the pond.
  it("lets every cat be tapped once its model loads, the player's own included", async () => {
    const loaded = deferred<true>();
    const view = new RoomScene(new NullEngine(), async (scene) => {
      await loaded.promise;
      return loadCatModel(scene, modelBytes());
    });
    view.playerId = "tom";
    view.show({ ...room, cats: [{ playerId: "tom", displayName: "Tom", furColor: "Grey", x: 0, z: 0, pose: null, hat: null, collar: null }] });
    view.place({ playerId: "felix", displayName: "Felix", furColor: "Black", x: 2, z: 0, pose: null, hat: null, collar: null });

    loaded.resolve(true);
    await vi.waitFor(() => expect(view.animationOf("felix")).toBeDefined());
    view.place({ playerId: "garfield", displayName: "Garfield", furColor: "Orange", x: -2, z: 0, pose: null, hat: null, collar: null });
    await vi.waitFor(() => expect(view.animationOf("garfield")).toBeDefined());
    expect(view.animationOf("tom")).toBeDefined();

    const meshes = (view as unknown as { scene: Scene }).scene.meshes;
    const pickable = (id: string) => [...new Set(meshes
      .filter((mesh) => catPlayerIdOf(mesh) === id && !mesh.name.startsWith("anchor-"))
      .map((mesh) => mesh.isPickable))];
    expect(pickable("felix")).toEqual([true]);
    expect(pickable("garfield")).toEqual([true]);
    expect(pickable("tom")).toEqual([true]);
    expect(meshes.filter((mesh) => mesh.name.startsWith("anchor-")).every((anchor) => !anchor.isPickable)).toBe(true);
  });

  // A room just shown, before its first frame, already says where things are drawn: the end-to-end tests tap a spot as
  // soon as the room shows (the camera's matrices were only worked out by a render).
  it("says where a spot is drawn before the room's first frame", () => {
    const view = new RoomScene(new NullEngine(), () => new Promise<AssetContainer>(() => {}));
    view.show(room);

    const point = view.screenPoint(1.4, -0.6);

    expect(Number.isFinite(point?.x)).toBe(true);
    expect(Number.isFinite(point?.y)).toBe(true);
  });

  // Where a cat is drawn, as it walks: not where it was sent (the catalogue opens at once only for a cat drawn near it).
  it("says where a cat is drawn, not where it is walking to", () => {
    const view = new RoomScene(new NullEngine(), () => new Promise<AssetContainer>(() => {}));
    view.show({ ...room, cats: [{ playerId: "tom", displayName: "Tom", furColor: "Grey", x: -3, z: 1, pose: null, hat: null, collar: null }] });

    view.walk("tom", 4, -2);

    expect(view.positionOf("tom")).toEqual({ x: -3, z: 1 });
    expect(view.positionOf("nobody")).toBeUndefined();
  });

  // The names and bubbles are a GUI layer over the canvas, which takes Tab for its own controls while the canvas has
  // the focus (a tap on the room gives it the focus): it has none, and Tab must go on to the page's buttons.
  it("leaves Tab to the page: the names and bubbles take no keyboard focus", () => {
    const view = new RoomScene(new NullEngine(), () => new Promise<AssetContainer>(() => {}));
    view.show(room);

    const gui = (view as unknown as { gui: { disableTabNavigation: boolean } }).gui;
    expect(gui.disableTabNavigation).toBe(true);
  });

  // While old and new servers run side by side in a rolling deploy, an old one sends a room with no props at all.
  it("shows a room whose snapshot has no props", () => {
    const view = new RoomScene(new NullEngine(), () => new Promise<AssetContainer>(() => {}));

    expect(() => view.show({ ...room, props: null })).not.toThrow();
  });

  // Props are decoration: the wall and the props are one mesh, which a tap on a prop walks the cat beside, and the
  // backdrop (which a tap does nothing on) another, never ray-tested: two draw calls.
  it("draws the wall and props as one mesh that takes taps, the backdrop as one that doesn't, and keeps the pond and mats their colours", () => {
    const view = new RoomScene(new NullEngine(), () => new Promise<AssetContainer>(() => {}));
    view.show({
      ...room,
      pond: { x: -5, z: -2, radius: 2 },
      doors: [{ id: "cafe", x: 6, z: 5, radius: 0.8, toRoomId: "cafe", toRoomName: "Cat Café" }],
      props: [
        { kind: "bench", x: 6, z: -3, radius: 1, facing: 0 },
        { kind: "tree", x: -8, z: 4, radius: 1.4, facing: 0 },
        { kind: "statue", x: 0, z: -4, radius: 1, facing: 0 },
      ],
    });

    const scene = (view as unknown as { scene: Scene }).scene;
    const mesh = (name: string) => scene.getMeshByName(name);
    expect(scene.meshes.filter((m) => m.name === "decor" || m.name === "backdrop")).toHaveLength(2);
    expect(mesh("decor")?.isPickable).toBe(true);
    expect(mesh("backdrop")?.isPickable).toBe(false);
    expect(mesh("floor")?.isPickable).toBe(true);
    expect(mesh("pond")?.isPickable).toBe(true);
    expect(mesh("door-cafe-mat")?.isPickable).toBe(true);
    const colour = (name: string) => (mesh(name)?.material as StandardMaterial | null)?.emissiveColor.toHexString();
    expect(colour("pond")).toBe("#5FB3D9");
    expect(colour("door-cafe-mat")).toBe("#C8553D");
  });

  it("keeps the shoal within the fishing water and lets taps reach the pond", () => {
    const engine = new NullEngine();
    const view = new RoomScene(engine, () => new Promise<AssetContainer>(() => {}));
    const pond = { x: -3, z: -2, radius: 2.4 };
    setLooks({
      rooms: [{ id: "stream", look: { sky: "#c1e2ef", floorColour: "#9fd38a", wallColour: "#ac805b", wildlife: "fish", backdrop: { parts: [] } } }],
      variants: [],
    });
    view.show({ ...room, roomId: "stream", pond });
    const scene = (view as unknown as { scene: Scene }).scene;
    const shoal = scene.getMeshByName("wildlife");
    expect(shoal?.isPickable).toBe(false);
    if (!shoal) throw new Error("no fish");
    (view as unknown as { frameSeconds: number }).frameSeconds = 0.05;
    const start = shoal.position.clone();
    for (let i = 0; i < 80; i++) {
      scene.onBeforeRenderObservable.notifyObservers(scene);
      expect(Math.hypot(shoal.position.x - pond.x, shoal.position.z - pond.z)).toBeLessThan(pond.radius - 0.5);
    }
    expect(shoal.position.equals(start)).toBe(false);
    view.show(room);
    expect(shoal.isDisposed()).toBe(true);
    setLooks(JSON.parse(seed) as LooksData);
  });

  describe("a tap", () => {
    // Everything a tap's ray goes through, nearest first, as the scene's multiPick finds them.
    function setUp() {
      const view = new RoomScene(new NullEngine(), () => new Promise<AssetContainer>(() => {}));
      const heard: string[] = [];
      view.onFloorClicked((x, z) => heard.push(`walk ${x.toFixed(2)} ${z.toFixed(2)}`));
      view.onDoorClicked((door) => heard.push(`door ${door.id}`));
      view.onCatClicked((id) => heard.push(`cat ${id}`));
      view.onPondClicked(() => heard.push("pond"));
      view.onStandClicked((stand) => heard.push(`stand ${stand.x} ${stand.z}`));
      view.playerId = "tom";
      view.show({
        ...room,
        pond: { x: -5, z: -2, radius: 2 },
        doors: [{ id: "cafe", x: 6, z: 5, radius: 0.8, toRoomId: "cafe", toRoomName: "Cat Café" }],
        props: [
          { kind: "tree", x: 6, z: 2, radius: 1, facing: 0 },
          { kind: "cafe-table", x: -1, z: -3, radius: 1.2, facing: 0 },
          { kind: "catalogue-stand", x: 3, z: -3, radius: 0.6, facing: 0 },
        ],
      });
      view.place({ playerId: "felix", displayName: "Felix", furColor: "Black", x: -1, z: -3, pose: null, hat: null, collar: null });
      view.place({ playerId: "tom", displayName: "Tom", furColor: "Grey", x: 6, z: 3.5, pose: null, hat: null, collar: null });
      const scene = (view as unknown as { scene: Scene }).scene;
      const hit = (name: string, x: number, y: number, z: number): PickingInfo =>
        ({ hit: true, pickedMesh: scene.getMeshByName(name), pickedPoint: new Vector3(x, y, z) }) as PickingInfo;
      const tap = (...hits: PickingInfo[]) =>
        (view as unknown as { tap(picks: PickingInfo[]): void }).tap(hits.map((h, i) => Object.assign(h, { distance: i + 1 })));
      return { heard, hit, tap };
    }

    // A tree between the camera and the door: the door mat still shows round it, and a tap that reaches it goes through.
    it("goes through a door behind a prop", () => {
      const { heard, hit, tap } = setUp();

      tap(hit("decor", 6, 3, 2.5), hit("door-cafe-mat", 6, 0.04, 5), hit("floor", 6, 0, 5));

      expect(heard).toEqual(["door cafe"]);
    });

    // Cats walk through props: a cat sitting among a table's chairs still opens its card.
    it("opens the card of a cat at a prop", () => {
      const { heard, hit, tap } = setUp();

      tap(hit("decor", -1, 0.7, -3.3), hit("anchor-felix", -1, 0.5, -3), hit("floor", -1, 0, -2.8));

      expect(heard).toEqual(["cat felix"]);
    });

    // The player's own cat, tapped: its own card; but a door or the pond behind it still takes the tap.
    it("on the player's own cat opens its card, unless a door or the pond is behind it", () => {
      const { heard, hit, tap } = setUp();

      tap(hit("anchor-tom", 6, 0.5, 3.5), hit("floor", 6, 0, 3.6));
      tap(hit("anchor-tom", 6, 0.5, 3.5), hit("decor", 6, 1, 3.4), hit("floor", 6, 0, 3.6));
      tap(hit("anchor-tom", 6, 0.5, 3.5), hit("door-cafe-mat", 6, 0.04, 5), hit("floor", 6, 0, 5));
      tap(hit("anchor-tom", -5, 0.5, -2), hit("pond", -5, 0.04, -2), hit("floor", -5, 0, -2));
      tap(hit("anchor-tom", 6, 0.5, 3.5), hit("anchor-felix", -1, 0.5, -3));

      expect(heard).toEqual(["cat tom", "cat tom", "door cafe", "pond", "cat felix"]);
    });

    // The catalogue stand: tapped, or the floor it stands on (it is small, for a finger), it opens the catalogue.
    it("on the catalogue stand, or the floor under it, reads the catalogue, not walking beside it", () => {
      const { heard, hit, tap } = setUp();

      tap(hit("decor", 3.1, 1, -3.2), hit("floor", 3.1, 0, -2));
      tap(hit("floor", 2.7, 0, -3.3));
      tap(hit("door-cafe-mat", 6, 0.04, 5), hit("decor", 3.1, 1, -3.2));

      expect(heard).toEqual(["stand 3 -3", "stand 3 -3", "door cafe"]);
    });

    it("on a prop's top walks the cat beside the prop, not to the floor behind it", () => {
      const { heard, hit, tap } = setUp();

      tap(hit("decor", 6.5, 3, 2), hit("floor", 6.5, 0, 4));

      expect(heard).toEqual(["walk 7.40 2.00"]);
    });

    it("on the wall or the backdrop does nothing", () => {
      const { heard, hit, tap } = setUp();

      tap(hit("decor", 0, 2, 6));

      expect(heard).toEqual([]);
    });

    it("on the floor walks there, and on the pond fishes", () => {
      const { heard, hit, tap } = setUp();

      tap(hit("floor", 2, 0, -1));
      tap(hit("pond", -5, 0.04, -2), hit("floor", -5, 0, -2));

      expect(heard).toEqual(["walk 2.00 -1.00", "pond"]);
    });
  });


  // WebGL1 can't build mipmaps for a texture whose sides aren't powers of two (it samples black instead).
  it("paints every surface onto a canvas whose sides are powers of two", () => {
    const engine = new NullEngine();
    const canvases = vi.spyOn(engine, "createCanvas");
    const view = new RoomScene(engine, () => new Promise<AssetContainer>(() => {}));
    canvases.mockClear();
    // Sizes no other test paints, so nothing is kept from before.
    view.show({ ...room, roomId: "town-square", width: 23, depth: 15 });
    view.show({ ...room, roomId: "cafe", width: 13, depth: 9 });

    const sizes = canvases.mock.calls.filter(([width, height]) => width > 1 || height > 1);
    expect(sizes.length).toBeGreaterThanOrEqual(4);
    const powerOfTwo = (n: number) => Number.isInteger(Math.log2(n));
    for (const [width, height] of sizes) {
      expect(powerOfTwo(width) && powerOfTwo(height), `${width}x${height}`).toBe(true);
    }
  });

  // Only a door by the back wall has a doorway in it, so only those doors change how the wall is painted.
  it("paints the wall round the doorways it draws, not round doors out on the floor", () => {
    const look = lookOf("town-square");
    const paintWall = vi.spyOn(look, "paintWall");
    try {
      const view = new RoomScene(new NullEngine(), () => new Promise<AssetContainer>(() => {}));
      const byTheWall = { id: "cafe", x: 6, z: 7, radius: 0.8, toRoomId: "cafe", toRoomName: "Cat Café" };
      const outFront = { id: "park", x: -6, z: -7, radius: 0.8, toRoomId: "park", toRoomName: "Park" };
      view.show({ ...room, roomId: "town-square", width: 21, depth: 16, doors: [byTheWall, outFront] });

      expect(paintWall).toHaveBeenCalledWith(expect.anything(), 21, 4, expect.any(Number), [byTheWall]);
    } finally {
      paintWall.mockRestore();
    }
  });
  it("leaves the back wall out of a room that has a path out of its north edge, so the path isn't walled off", () => {
    const look = lookOf("town-square");
    const paintWall = vi.spyOn(look, "paintWall");
    try {
      const view = new RoomScene(new NullEngine(), () => new Promise<AssetContainer>(() => {}));
      const chunk = { roomId: "town-square", name: "Town Square", x: 0, z: 0, width: 24, depth: 16, doors: [], props: [], pond: null, look: null };
      // Widths no other test paints (a wall is painted once per width), so the calls here are this test's own.
      view.show({ ...room, roomId: "town-square", width: 26, depth: 16, chunks: [{ ...chunk, width: 26 }] });
      expect(paintWall).toHaveBeenCalledTimes(1);
      paintWall.mockClear();
      view.show({ ...room, roomId: "town-square", width: 28, depth: 16, chunks: [{ ...chunk, width: 28, paths: [{ x: 0, z: 14, width: 3, depth: 12 }] }] });

      expect(paintWall).not.toHaveBeenCalled();
    } finally {
      paintWall.mockRestore();
    }
  });

  // The paintings are the same every time (seeded), so a room seen again reuses them rather than painting them anew.
  it("paints a room's floor, wall and street once, however often it shows", () => {
    const look = lookOf("town-square");
    const painters = [vi.spyOn(look, "paintFloor"), vi.spyOn(look, "paintWall"), vi.spyOn(look.ground?.paint ? look.ground : { paint: () => {} }, "paint")];
    try {
      const view = new RoomScene(new NullEngine(), () => new Promise<AssetContainer>(() => {}));
      const square = { ...room, roomId: "town-square", width: 24, depth: 16 };
      view.show(square);
      // Painted by now: just then, or by a room shown before.
      const painted = painters.map((painter) => painter.mock.calls.length);
      view.show({ ...room, roomId: "cafe", width: 14, depth: 10 });
      view.show(square);

      expect(painters.map((painter) => painter.mock.calls.length)).toEqual(painted);
      expect(painted[0]).toBe(1);
    } finally {
      painters.forEach((painter) => painter.mockRestore());
    }
  });
});

describe("RoomScene dressing", () => {
  beforeEach(() => vi.stubGlobal("OffscreenCanvas", NoCanvas));
  afterEach(() => vi.unstubAllGlobals());

  it("dresses a cat as it arrives, and as it changes, also while its model still loads; another cat's hat takes taps", async () => {
    const loaded = deferred<true>();
    const view = new RoomScene(new NullEngine(), async (scene) => {
      await loaded.promise;
      return loadCatModel(scene, modelBytes());
    });
    view.playerId = "tom";
    view.show({ ...room, cats: [{ playerId: "felix", displayName: "Felix", furColor: "Black", x: 2, z: 0, pose: null, hat: "bow", collar: null }] });
    view.dress({ playerId: "felix", furColor: "Calico", hat: "crown", collar: "blue-collar" });

    loaded.resolve(true);
    await vi.waitFor(() => expect(view.wearingOf("felix")).toEqual({ fur: "Calico", hat: "crown", collar: "blue-collar" }));

    view.dress({ playerId: "felix", furColor: "Calico", hat: null, collar: "blue-collar" });
    expect(view.wearingOf("felix")).toEqual({ fur: "Calico", hat: undefined, collar: "blue-collar" });
    // A join for a cat already here (its other tab) brings what it wears now.
    view.place({ playerId: "felix", displayName: "Felix", furColor: "Calico", x: 2, z: 0, pose: null, hat: "party-hat", collar: null });
    expect(view.wearingOf("felix")).toEqual({ fur: "Calico", hat: "party-hat", collar: undefined });
    const meshes = (view as unknown as { scene: Scene }).scene.meshes;
    const hat = meshes.filter((mesh) => mesh.name.startsWith("cat-felix-hat"));
    expect(hat.length).toBeGreaterThan(0);
    expect(hat.every((mesh) => mesh.isPickable && catPlayerIdOf(mesh) === "felix")).toBe(true);
    view.dress({ playerId: "nobody", furColor: "Grey", hat: "crown", collar: null });
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

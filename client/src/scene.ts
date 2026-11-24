// Babylon by module, not from its index, so the page loads only what it uses.
import { ArcRotateCamera } from "@babylonjs/core/Cameras/arcRotateCamera";
import type { AssetContainer } from "@babylonjs/core/assetContainer";
import type { AbstractEngine } from "@babylonjs/core/Engines/abstractEngine";
import type { ICanvas } from "@babylonjs/core/Engines/ICanvas";
import { StandardMaterial } from "@babylonjs/core/Materials/standardMaterial";
import { DynamicTexture } from "@babylonjs/core/Materials/Textures/dynamicTexture";
import { Color3, Color4 } from "@babylonjs/core/Maths/math.color";
import { Matrix, Vector3 } from "@babylonjs/core/Maths/math.vector";
import type { AbstractMesh } from "@babylonjs/core/Meshes/abstractMesh";
import { CreateBox } from "@babylonjs/core/Meshes/Builders/boxBuilder";
import { CreateCylinder } from "@babylonjs/core/Meshes/Builders/cylinderBuilder";
import { CreateGround } from "@babylonjs/core/Meshes/Builders/groundBuilder";
import { CreatePlane } from "@babylonjs/core/Meshes/Builders/planeBuilder";
import { CreateTorus } from "@babylonjs/core/Meshes/Builders/torusBuilder";
import type { Mesh } from "@babylonjs/core/Meshes/mesh";
import { TransformNode } from "@babylonjs/core/Meshes/transformNode";
import type { Node as BabylonNode } from "@babylonjs/core/node";
import type { PickingInfo } from "@babylonjs/core/Collisions/pickingInfo";
import { Scene } from "@babylonjs/core/scene";
// For its side effects: picking what a pointer touches.
import "@babylonjs/core/Culling/ray";
import { AdvancedDynamicTexture } from "@babylonjs/gui/2D/advancedDynamicTexture";
import { Rectangle } from "@babylonjs/gui/2D/controls/rectangle";
import { TextBlock } from "@babylonjs/gui/2D/controls/textBlock";
import { cameraFocus, cameraPitch, cameraRadius, fieldOfView, roomPitch, startingZoom, zoomRadius } from "./view";
import { wildlife } from "./wildlife";
import { sceneryTarget } from "./walking";
import { animationFor, Cat, facing, loadCatModel, MeowClip, type CatAnimation, type Outfit } from "./cat";
import { hasDoorway, openLandscape, pathEdge } from "./doors";
import { chunkAt, clampToWorld, place, worldBounds, type PlacedChunk } from "./overworld";
import { lazily } from "./lazy";
import { appLog } from "./telemetry";
import { lookOf } from "./looks";
import {
  floorPixelsPerMetre, powerOfTwo, streetTile, textureSize, wallPixelsPerMetre, type Brush, type Floorplan,
} from "./paint";
import { activityName, RoomActivities } from "./roomActivities";
import { catalogueStandKind } from "./catalogue";
import { Portraits } from "./portrait";
import { besideProp, buildParts, ink, propAt, propParts, type Part } from "./props";
import { CatLook } from "./look";
import catModelUrl from "./models/cat.glb?url";
import type { CatView, ChunkView, DoorView, Emote, OutfitView, PropView, RoomSnapshot } from "./types";
import { frameClock, frameLimiter } from "./frameLimit";

const walkSpeed = 4; // metres per second
const sky = Color4.FromHexString("#bfe3f2ff");
// Where the name and bubbles hang: just over the tips of the ears.
const anchorHeight = 1.4;
/** A door's mat: its own red, which nothing else in a room is (the layout test looks for it). */
export const doorMatColour = "#c8553d";
/** The terracotta paving on outdoor paths, which the room layout tests look for. */
export const pathColour = "#d9a468";
/** The ring round the furniture selected while decorating: the buttons' orange. */
const selectionColour = "#f0a04b";
/** How close (in metres) a cat drawn walking somewhere counts as there. */
const there = 0.05;
/** How tall a room's back wall is, in metres. */
const wallHeight = 4;
/** How much floor the open world's camera fits at its furthest zoom, in metres: about the distance a cat sees (WorldSpace.ViewDistance). */
const openViewWidth = 48;
const openViewDepth = 30;

// The cat model's bytes: fetched once, on the way into the first room; every room reads them into its own scene. A
// fetch that failed is tried again by the next room.
const catModel = lazily(async () => {
  const response = await fetch(catModelUrl);
  if (!response.ok) {
    throw new Error(`The cat model didn't load: ${response.status}`);
  }
  return new Uint8Array(await response.arrayBuffer());
});

/** The cat model, read into <scene>. */
async function catModelFor(scene: Scene): Promise<AssetContainer> {
  return loadCatModel(scene, await catModel());
}

interface CatOnScreen {
  // Where the cat stands and which way it faces; the model, once loaded, hangs off it.
  root: TransformNode;
  anchor: AbstractMesh;
  target: Vector3;
  look: CatLook;
  bubble?: { box: Rectangle; text: string };
  model?: Cat;
  // What it wears: put on the model once it's drawn, and whenever it changes.
  outfit: Outfit;
  meow: MeowClip;
  // Someone waiting for the cat to be drawn standing at a spot (a walk to a door), told true when it is, false if it
  // is sent elsewhere first.
  arrival?: { x: number; z: number; done: (arrived: boolean) => void };
}

/** Draws one room at a time and walks the cats in it. Knows nothing about the network. */
export class RoomScene {
  private scene: Scene;
  private gui: AdvancedDynamicTexture | undefined;
  private frameSeconds = 0;
  private readonly cats = new Map<string, CatOnScreen>();
  // The cat model, read into the scene showing.
  private model: Promise<AssetContainer | undefined> = Promise.resolve(undefined);
  private floorClicked: (x: number, z: number) => void = () => {};
  private pondClicked: () => void = () => {};
  private doorClicked: (door: DoorView) => void = () => {};
  private catClicked: (playerId: string) => void = () => {};
  private standClicked: (stand: PropView) => void = () => {};
  private editTapped: (x: number, z: number) => void = () => {};

  // The room showing's back wall and props (one mesh, rebuilt when a home's furniture changes), and the ring round the
  // piece of furniture selected while decorating.
  private walls: Part[] = [];
  // The chunks showing in world metres: a room alone is one at the middle.
  private chunks: PlacedChunk[] | undefined;
  private groundPatches = 0;
  private readonly doorLabels: { label: TextBlock; spot: Vector3; length: number }[] = [];
  private roomEntered: ((chunk: ChunkView) => void) | undefined;
  private decor: Mesh | undefined;
  private activities = new RoomActivities();
  private activityClicked: (prop: PropView) => void = () => {};
  private props: PropView[] = [];
  private selection: Mesh | undefined;
  /**
   * Decorating a home: a tap goes to the furniture or the floor under it (onEditTapped), never to a cat, a door or the
   * pond, and walks no one.
   */
  editing = false;
  // What a tap does, given everything along its ray (multiPick): set for the room showing.
  private tap: (picks: readonly PickingInfo[], outside?: () => { x: number; z: number } | undefined) => void = () => {};
  private pickScreen: (x: number, y: number) => void = () => {};
  private changeZoom: (factor: number) => void = () => {};
  private zoom: number | undefined;
  /**
   * The player's own cat: a tap on it opens the player's own card, but only when nothing else a tap goes to is along its
   * ray (another cat, a door, the pond): a tap on a door just behind it still goes through the door.
   */
  playerId: string | undefined;

  /** The room editor's preview: the room being shown is edged in pink and named, so it can be found among its neighbours. */
  markArrival = false;

  /** Pictures of cats for their cards, taken off screen with this engine and the cat model. */
  readonly portraits: Portraits;

  /** <loadModel> reads the cat model into a room's scene (the tests hand in their own). */
  constructor(
    private readonly engine: AbstractEngine,
    private readonly loadModel: (scene: Scene) => Promise<AssetContainer> = catModelFor,
    private readonly touch = false,
    maxFps: () => number | undefined = () => undefined,
  ) {
    this.scene = new Scene(this.engine);
    this.scene.clearColor = sky;
    this.portraits = new Portraits(this.engine, this.loadModel);
    // One loop for the page; it draws whichever room is showing.
    const due = frameLimiter(maxFps);
    const clock = frameClock();
    this.engine.runRenderLoop(() => {
      const now = performance.now();
      if (due(now)) {
        this.frameSeconds = clock(now);
        this.renderFrame();
      }
    });
  }

  /** Draws one frame of whatever is showing. Until a room shows there is no camera: the canvas keeps its sky. */
  renderFrame(): void {
    if (this.scene.activeCamera) {
      this.scene.render();
    }
  }

  tapScreen(x: number, y: number): void {
    this.pickScreen(x, y);
  }

  zoomBy(factor: number): void {
    if (factor !== 1) {
      this.changeZoom(factor);
    }
  }

  onFloorClicked(handler: (x: number, z: number) => void): void {
    this.floorClicked = handler;
  }

  /** Told when the player's cat walks into another room of the open world. */
  onRoomEntered(handler: (chunk: ChunkView) => void): void {
    this.roomEntered = handler;
  }

  onPondClicked(handler: () => void): void {
    this.pondClicked = handler;
  }

  onDoorClicked(handler: (door: DoorView) => void): void {
    this.doorClicked = handler;
  }

  /** A tap or click on a cat (the player's own included): its player's ID. */
  onCatClicked(handler: (playerId: string) => void): void {
    this.catClicked = handler;
  }

  /** A tap or click on the shop's catalogue stand (or the floor it stands on). */
  onStandClicked(handler: (stand: PropView) => void): void {
    this.standClicked = handler;
  }

  onActivityClicked(handler: (prop: PropView) => void): void {
    this.activityClicked = handler;
  }

  approach(prop: PropView, room: RoomSnapshot): { x: number; z: number } | undefined {
    return this.besideAt(prop.x, prop.z) ?? besideProp(this.props, room, prop.x, prop.z);
  }

  tend(prop: PropView): string {
    const before = this.activities.stage(prop);
    const message = this.activities.tend(prop, this.props.filter(p => p.kind === "pear-tree").length);
    if (before !== this.activities.stage(prop)) this.drawDecor();
    return message;
  }

  /** A tap while decorating: where on the floor, under the furniture or the floor tapped. */
  onEditTapped(handler: (x: number, z: number) => void): void {
    this.editTapped = handler;
  }

  /** The nearest furniture or floor along the ray (not the wall, which is in the same mesh): the spot on the floor under it. */
  private editTap(hits: PickingInfo[], floors: ReadonlySet<AbstractMesh>, decor: AbstractMesh | undefined): void {
    const onFurniture = (point: Vector3) => this.props.some((p) => Math.hypot(point.x - p.x, point.z - p.z) <= p.radius);
    const spot = hits.map((pick) => (pick.pickedMesh && floors.has(pick.pickedMesh)) || (pick.pickedMesh === decor && pick.pickedPoint && onFurniture(pick.pickedPoint))
      ? pick.pickedPoint
      : null).find((point) => point);
    if (spot) {
      this.editTapped(spot.x, spot.z);
    }
  }

  /** Replaces whatever room was showing. */
  show(room: RoomSnapshot): void {
    for (const cat of this.cats.values()) {
      cat.arrival?.done(false);
    }
    this.cats.clear();
    this.editing = false;
    this.selection = undefined;
    this.scene.dispose();
    this.scene = new Scene(this.engine);
    this.groundPatches = 0;
    this.doorLabels.length = 0;
    this.gui = AdvancedDynamicTexture.CreateFullscreenUI("ui", true, this.scene);
    // Its names and bubbles take no focus: left on, it took Tab for itself whenever the canvas had the focus (after a tap
    // on the room), and the keyboard could no longer reach the page's buttons.
    this.gui.disableTabNavigation = true;
    const scene = this.scene;
    this.model = this.loadModel(scene)
      .then((model) => {
        // A room left while its model loaded: the scene and its dispose hooks are gone; nothing else would free it.
        if (scene.isDisposed) {
          model.dispose();
          return undefined;
        }
        scene.onDisposeObservable.add(() => model.dispose());
        return model;
      })
      .catch((error: unknown) => {
        // The room still works without its cats drawn: their names and bubbles show. A room already left doesn't
        // matter (reading into a scene that's gone fails).
        if (!scene.isDisposed) {
          appLog.error("The cat model didn't load", error);
        }
        return undefined;
      });

    // Keep the viewing direction steady while zooming and following the cat.
    const camera = new ArcRotateCamera("camera", -Math.PI / 2, cameraPitch, 1, Vector3.Zero(), this.scene);
    camera.fov = fieldOfView;
    // The open world: every outdoor room in place, and a camera that follows the cat over all of it. A room alone is the
    // same, as one chunk at the middle.
    const chunks = (room.chunks?.length
      ? room.chunks
      : [{ roomId: room.roomId, name: room.roomName, x: 0, z: 0, width: room.width, depth: room.depth, pond: room.pond,
        doors: room.doors, props: room.props ?? [], look: room.look ?? null }]).map(place);
    const world = room.chunks?.length ? chunks : undefined;
    this.chunks = chunks;
    const arrival = chunks.find((chunk) => chunk.roomId === room.roomId) ?? chunks[0];
    if (!arrival) {
      throw new Error("The room has no floor.");
    }
    const view = world ? { width: openViewWidth, depth: openViewDepth } : room;
    let aspect = this.engine.getAspectRatio(camera);
    let fitted = cameraRadius(aspect, view.width, view.depth);
    const aim = () => {
      const cat = this.playerId === undefined ? undefined : this.positionOf(this.playerId);
      if (world) {
        const focus = cat ?? arrival;
        camera.target.set(focus.x, 0, focus.z);
        return;
      }
      if (camera.radius === fitted) {
        return;
      }
      const focus = cameraFocus(room, cat, aspect, camera.radius, fitted);
      camera.target.set(focus.x, 0, focus.z);
    };
    const fit = () => {
      aspect = this.engine.getAspectRatio(camera);
      fitted = cameraRadius(aspect, view.width, view.depth);
      camera.beta = roomPitch(aspect);
      camera.radius = zoomRadius(fitted, this.zoom ?? startingZoom(aspect, this.touch));
      camera.target.setAll(0);
      aim();
    };
    this.changeZoom = (factor) => {
      // Start from the actual camera, so coming out of the closest view responds to the first gesture.
      this.zoom = Math.max(1, Math.min(fitted / 9, (fitted / camera.radius) * factor));
      fit();
    };
    fit();
    this.engine.onResizeObservable.add(fit);
    this.scene.onDisposeObservable.add(() => this.engine.onResizeObservable.removeCallback(fit));
    camera.inputs.clear();

    const floors = new Set<AbstractMesh>();
    const ponds = new Set<AbstractMesh>();
    const doors = new Map<AbstractMesh, DoorView>();
    const walls: Part[] = [];
    const animations: ((seconds: number) => void)[] = [];
    const skies = new Map<string, Color4>();
    const baseGround = world ? groundChunkOf(chunks, arrival) : arrival;
    for (const chunk of chunks) {
      this.drawChunk(chunk, world ? chunks : undefined, { floors, ponds, doors, walls, animations, skies, baseGround, marked: this.markArrival ? arrival : undefined });
    }
    this.walls = walls;
    this.activities = new RoomActivities();
    this.props = chunks.flatMap((chunk) => chunk.props);
    this.decor = undefined;
    this.drawDecor();
    this.scene.clearColor = skies.get(arrival.roomId) ?? sky;

    // A tap goes to the first thing along its ray in this order, not to the nearest: another cat, a door, the pond, the
    // player's own cat, the catalogue stand, a prop (beside it), the floor or its edge. Props are tall and cats walk through them: a
    // cat among a table's chairs, or a door mat showing round a tree, still takes the tap; and the player's own cat
    // stands in front of the door it walks to.
    this.tap = (picks, outside) => {
      const hits = picks.filter((pick) => pick.hit && pick.pickedMesh).sort((a, b) => a.distance - b.distance);
      const decor = this.decor;
      if (this.editing) {
        this.editTap(hits, floors, decor);
        return;
      }
      const catIds = hits.map((pick) => catPlayerIdOf(pick.pickedMesh));
      const catId = catIds.find((id) => id !== undefined && id !== this.playerId);
      const ownCat = this.playerId !== undefined && catIds.includes(this.playerId) ? this.playerId : undefined;
      const door = hits.map((pick) => (pick.pickedMesh ? doors.get(pick.pickedMesh) : undefined)).find((d) => d);
      const onProps = hits.filter((pick) => decor && pick.pickedMesh === decor && pick.pickedPoint);
      const ground = hits.find((pick) => pick.pickedMesh && floors.has(pick.pickedMesh))?.pickedPoint;
      // The stand, tapped, or the floor it stands on (it is small, to tap on a phone).
      const pickedProp = [...onProps.map((pick) => pick.pickedPoint), ground]
        .map((point) => (point ? propAt(this.props, point.x, point.z) : undefined))
        .find((prop) => prop && (prop.kind === catalogueStandKind || activityName(prop)));
      const beside = onProps
        .map((pick) => this.besideAt(pick.pickedPoint?.x ?? 0, pick.pickedPoint?.z ?? 0)).find((spot) => spot);
      if (catId !== undefined) {
        this.catClicked(catId);
      } else if (door) {
        this.doorClicked(door);
      } else if (hits.some((pick) => pick.pickedMesh && ponds.has(pick.pickedMesh))) {
        this.pondClicked();
      } else if (ownCat !== undefined) {
        this.catClicked(ownCat);
      } else if (pickedProp?.kind === catalogueStandKind) {
        this.standClicked(pickedProp);
      } else if (pickedProp) {
        this.activityClicked(pickedProp);
      } else if (beside) {
        this.floorClicked(beside.x, beside.z);
      } else {
        const point = ground ?? outside?.();
        if (point) {
          this.floorClicked(point.x, point.z);
        }
      }
    };
    this.pickScreen = (x, y) => {
      this.tap(this.scene.multiPick(x, y) ?? [], () => {
        const cat = this.playerId === undefined ? undefined : this.positionOf(this.playerId);
        if (cat) {
          const ray = this.scene.createPickingRay(x, y, Matrix.Identity(), camera);
          if (world) {
            // Past the floor's edge: the nearest spot of the world's floor under where the tap points.
            const distance = ray.direction.y < -0.0001 ? -ray.origin.y / ray.direction.y : 0;
            return clampToWorld(world, { x: ray.origin.x + ray.direction.x * distance, z: ray.origin.z + ray.direction.z * distance });
          }
          return sceneryTarget(cat, ray, room);
        }
      });
    };
    this.scene.onPointerDown = (event) => {
      if (event.pointerType !== "touch" && event.button === 0) {
        this.tapScreen(this.scene.pointerX, this.scene.pointerY);
      }
    };
    let skyOf = arrival.roomId;
    this.scene.onBeforeRenderObservable.add(() => {
      const seconds = this.frameSeconds;
      this.step(seconds);
      this.stackDoorLabels();
      for (const animate of animations) {
        animate(seconds);
      }
      aim();
      // The sky of the room the cat is in.
      const cat = world && this.playerId !== undefined ? this.positionOf(this.playerId) : undefined;
      const here = cat && chunkAt(chunks, cat);
      if (here && here.roomId !== skyOf) {
        skyOf = here.roomId;
        this.scene.clearColor = skies.get(skyOf) ?? sky;
        this.roomEntered?.(here);
      }
    });

    for (const cat of room.cats) {
      this.place(cat);
    }
    aim();
  }

  private drawChunk(
    chunk: PlacedChunk,
    world: PlacedChunk[] | undefined,
    drawn: {
      floors: Set<AbstractMesh>; ponds: Set<AbstractMesh>; doors: Map<AbstractMesh, DoorView>; walls: Part[];
      animations: ((seconds: number) => void)[]; skies: Map<string, Color4>;
      baseGround: PlacedChunk; marked: PlacedChunk | undefined;
    },
  ): void {
    const { floors, ponds, doors, walls, animations, skies } = drawn;
    const look = lookOf(chunk.roomId, chunk.look);
    skies.set(chunk.roomId, Color4.FromHexString(`${look.sky}ff`));
    const { x: cx, z: cz, width, depth } = chunk;
    const { pond: roomPond, doors: roomDoors, props: roomProps } = chunk.local;
    const plan: Floorplan = { width, depth, pond: roomPond, doors: roomDoors, props: look.floorNeedsProps ? roomProps : undefined };
    const floor = CreateGround("floor", { width, height: depth }, this.scene);
    floor.position.set(cx, 0, cz);
    floor.material = look.paintFloor
      ? painted(this.scene, "floor", `${chunk.roomId}/floor/${look.floorVariant ?? ""}${look.paintKey ?? ""}/${JSON.stringify(plan)}`, textureSize(width, depth, floorPixelsPerMetre),
        (brush, scale) => look.paintFloor?.(brush, plan, scale))
      : flat(this.scene, "floor", look.floorColour);
    floors.add(floor);
    // The floor's dark edge: a little larger and just below it. Not renderOutline, whose pass, from far off (a narrow
    // screen backs the camera away), covered whatever lay flat on the floor, the pond.
    this.drawEdge(chunk, chunk === drawn.marked);
    this.drawGrounds(chunk, look, world, drawn.baseGround);
    // The back wall, outlined like the props (and with them), its front painted: a plane just in front of it (the
    // doorways stand in front of that).
    if (look.paintWall && !openLandscape(chunk.roomId) && !onAnotherRoom(world, chunk, cx, cz + depth / 2 + 0.15)) {
      const front = CreatePlane("back-wall-front", { width, height: wallHeight }, this.scene);
      front.position.set(cx, wallHeight / 2, cz + depth / 2 - 0.01);
      // Painted round the doorways in it (the doors by the wall: the ones drawDoor draws a doorway for).
      const doorways = roomDoors.filter((door) => hasDoorway(door, depth, chunk.roomId));
      front.material = painted(this.scene, "back-wall-front",
        `${chunk.roomId}/wall${look.paintKey ?? ""}/${width}/${JSON.stringify(doorways)}`, textureSize(width, wallHeight, wallPixelsPerMetre),
        (brush, scale) => look.paintWall?.(brush, width, wallHeight, scale, doorways));
      front.isPickable = false;
    }
    if (!openLandscape(chunk.roomId) && !onAnotherRoom(world, chunk, cx, cz + depth / 2 + 0.15)) {
      walls.push({ shape: "box", width, height: wallHeight, depth: 0.3, at: [cx, wallHeight / 2, cz + depth / 2 + 0.15], colour: look.wallColour });
    }
    // The backdrop is another mesh, which a tap never tests: it does nothing there.
    const backdrop = buildParts(this.scene, "backdrop",
      look.backdrop(plan, wallHeight).map((part) => ({ ...part, at: [part.at[0] + cx, part.at[1], part.at[2] + cz] as [number, number, number] }))
        .filter((part) => !onAnotherRoom(world, chunk, part.at[0], part.at[2])));
    if (backdrop) {
      backdrop.isPickable = false;
    }

    // The pond: a flat disc of water just above the floor. Clicking it fishes, rather than walking into it.
    if (chunk.pond) {
      // Two flat cylinders: the water, and a little larger and just below it, its dark edge, as the floor has. Not
      // renderOutline: on something this flat the outline pass painted over the whole of it.
      const edge = CreateCylinder("pond-edge", { diameter: chunk.pond.radius * 2 + 0.16, height: 0.02, tessellation: 48 }, this.scene);
      edge.position.set(chunk.pond.x, 0.01, chunk.pond.z);
      edge.material = flat(this.scene, "pond-edge", ink);
      edge.isPickable = false;
      const water = CreateCylinder("pond", { diameter: chunk.pond.radius * 2, height: 0.02, tessellation: 48 }, this.scene);
      water.position.set(chunk.pond.x, 0.03, chunk.pond.z);
      water.material = flat(this.scene, "pond", "#5fb3d9");
      ponds.add(water);
    }

    // The doors, each a mat on the floor (and, by the back wall, a doorway in it) that walks the cat through when
    // clicked. Drawn as the room has them, under a node at its middle; a tap gets the door in world metres.
    const at = new TransformNode(`doors-${chunk.roomId}`, this.scene);
    at.position.set(cx, 0, cz);
    roomDoors.forEach((door, index) => {
      const placed = chunk.doors[index] ?? door;
      for (const part of this.under(at, () => this.drawDoor(door, chunk))) {
        doors.set(part, placed);
      }
    });
    const animate = wildlife(this.scene, chunk, look.wildlife);
    if (animate) {
      animations.push(animate);
    }
  }

  /** A room's own ground in the open world, fading out over <groundBlend> metres past its edges so it melts into the ground of the room next to it. */
  private drawGroundPatch(
    look: NonNullable<ReturnType<typeof lookOf>["ground"]>, chunk: PlacedChunk, paintKey: string | undefined,
  ): void {
    const [width, depth] = [chunk.width + 2 * groundBlend, chunk.depth + 2 * groundBlend];
    const patch = CreateGround(`ground-${chunk.roomId}`, { width, height: depth }, this.scene);
    patch.position.set(chunk.x, -0.029 + this.groundPatches++ * 0.0004, chunk.z);
    patch.isPickable = false;
    const { colour, paint } = look;
    const material = paint
      ? painted(this.scene, "ground", `${chunk.roomId}/ground${paintKey ?? ""}`, textureSize(streetTile, streetTile, floorPixelsPerMetre), paint,
        { u: width / streetTile, v: depth / streetTile })
      : flat(this.scene, "ground", colour);
    const fade = new DynamicTexture(`ground-fade-${chunk.roomId}`, { width: 128, height: 128 }, this.scene, false);
    const context = fade.getContext();
    const pixels = context.getImageData(0, 0, 128, 128);
    for (let row = 0; row < 128; row++) {
      for (let column = 0; column < 128; column++) {
        const level = fadeAt(column / 127, row / 127, groundBlend / width, groundBlend / depth);
        pixels.data.set([level, level, level, 255], (row * 128 + column) * 4);
      }
    }
    context.putImageData(pixels, 0, 0);
    fade.update(false);
    fade.getAlphaFromRGB = true;
    material.opacityTexture = fade;
    patch.material = material;
  }

  /** A room's ground: the one under the whole world (or round a room alone), or, in the open world, a patch fading out round the room. */
  private drawGrounds(chunk: PlacedChunk, look: ReturnType<typeof lookOf>, world: PlacedChunk[] | undefined, baseGround: PlacedChunk): void {
    const { ground, paintKey } = look;
    if (!ground) {
      return;
    }
    if (chunk !== baseGround) {
      if (world && !sameGround(ground, lookOf(baseGround.roomId, baseGround.look).ground)) {
        this.drawGroundPatch(ground, chunk, paintKey);
      }
      return;
    }
    if (!world) {
      this.drawGround(ground, chunk, paintKey);
      return;
    }
    const { minX, maxX, minZ, maxZ } = worldBounds(world);
    this.drawGroundPatch(ground, { ...chunk, x: (minX + maxX) / 2, z: (minZ + maxZ) / 2, width: maxX - minX, depth: maxZ - minZ }, paintKey);
  }

  /** The ground round a room alone, below the floor's edge: out past where the camera sees. */
  private drawGround(look: NonNullable<ReturnType<typeof lookOf>["ground"]>, chunk: PlacedChunk, paintKey: string | undefined): void {
    const { x: cx, depth } = chunk;
    const [groundWidth, groundDepth] = [400, 200 + depth / 2 + 40];
    const ground = CreateGround("ground", { width: groundWidth, height: groundDepth }, this.scene);
    ground.position.set(cx, -0.03, (depth / 2 + 40 - 200) / 2);
    const { colour, paint } = look;
    ground.material = paint
      ? painted(this.scene, "ground", `${chunk.roomId}/ground${paintKey ?? ""}`, textureSize(streetTile, streetTile, floorPixelsPerMetre), paint,
        { u: groundWidth / streetTile, v: groundDepth / streetTile })
      : flat(this.scene, "ground", colour);
    ground.isPickable = false;
  }

  place(cat: CatView): void {
    const existing = this.cats.get(cat.playerId);
    if (existing) {
      this.sendTo(existing, cat.x, cat.z);
      existing.look.pose = cat.pose ?? undefined;
      this.dressOnScreen(existing, outfitOf(cat));
      return;
    }

    // The anchor (name, bubbles) hangs off the root, above whatever the model is doing.
    const name = `cat-${cat.playerId}`;
    const root = new TransformNode(name, this.scene);
    // Whose cat this is, for a tap on any of its meshes (another cat's take the pointer once its model is drawn).
    root.metadata = { catPlayerId: cat.playerId };
    root.position.set(cat.x, 0, cat.z);
    // Facing the camera until it walks somewhere.
    root.rotation.y = Math.PI;
    const anchor = CreateBox(`anchor-${cat.playerId}`, { size: 0.01 }, this.scene);
    anchor.parent = root;
    anchor.position.y = anchorHeight;
    anchor.isVisible = false;
    anchor.isPickable = false;

    const label = new TextBlock(`name-${cat.playerId}`, cat.displayName);
    label.color = ink;
    label.fontSize = 14;
    label.fontWeight = "bold";
    label.outlineColor = "white";
    label.outlineWidth = 3;
    this.gui?.addControl(label);
    label.linkWithMesh(anchor);
    label.linkOffsetY = -6;

    const look = new CatLook(cat.pose);
    const shown: CatOnScreen = {
      root, anchor, target: new Vector3(cat.x, 0, cat.z), look, meow: new MeowClip(), outfit: outfitOf(cat),
    };
    this.cats.set(cat.playerId, shown);
    const scene = this.scene;
    void this.model.then((model) => {
      // Not for a cat that left, or a room that's gone, meanwhile.
      if (model && this.cats.get(cat.playerId) === shown && this.scene === scene && !root.isDisposed()) {
        try {
          shown.model = new Cat(scene, model, root, name, shown.outfit.fur);
          this.dressOnScreen(shown, shown.outfit);
        } catch (error) {
          // That cat goes undrawn; its name and bubbles still show.
          console.error("A cat couldn't be drawn", error);
        }
      }
    });
  }

  /** The cat put on or took off something, or changed its fur. */
  dress(outfit: OutfitView): void {
    const cat = this.cats.get(outfit.playerId);
    if (cat) {
      this.dressOnScreen(cat, { fur: outfit.furColor, hat: outfit.hat, collar: outfit.collar });
    }
  }

  /** The cat's name above it changed. */
  rename(playerId: string, displayName: string): void {
    const label = this.gui?.getControlByName(`name-${playerId}`);
    if (label instanceof TextBlock) {
      label.text = displayName;
    }
  }

  /** A home's furniture changed: the room's props are drawn again as <props>, with the back wall (one mesh). */
  furnish(props: PropView[]): void {
    if (this.walls.length === 0) {
      return;
    }
    this.props = props;
    this.drawDecor();
  }

  /** Where a cat sent beside the prop at a spot (in world metres) stands, by the edges of the room the spot is in. */
  private besideAt(x: number, z: number): { x: number; z: number } | undefined {
    const chunk = this.chunks && (chunkAt(this.chunks, { x, z }) ?? this.chunks[0]);
    if (!chunk) {
      return undefined;
    }
    const props = this.props.map((p) => ({ ...p, x: p.x - chunk.x, z: p.z - chunk.z }));
    const spot = besideProp(props, chunk, x - chunk.x, z - chunk.z);
    return spot && { x: spot.x + chunk.x, z: spot.z + chunk.z };
  }

  /** Whatever <draw> makes that has no parent yet hangs off <node>. */
  private under<T>(node: TransformNode, draw: () => T): T {
    const known = new Set(this.scene.meshes);
    const drawn = draw();
    for (const mesh of this.scene.meshes) {
      if (!known.has(mesh) && !mesh.parent) {
        mesh.parent = node;
      }
    }
    return drawn;
  }

  private drawDecor(): void {
    this.decor?.dispose(false, true);
    const parts = this.props.flatMap(prop => propParts(prop, this.activities.stage(prop)));
    this.decor = buildParts(this.scene, "decor", [...this.walls, ...parts]);
  }

  /** Rings the piece of furniture selected while decorating, on the floor round it; none, no ring. */
  ring(prop: PropView | undefined): void {
    this.selection?.dispose(false, true);
    this.selection = undefined;
    if (!prop) {
      return;
    }
    const ring = CreateTorus("selection", { diameter: prop.radius * 2 + 0.1, thickness: 0.08, tessellation: 48 }, this.scene);
    ring.position.set(prop.x, 0.06, prop.z);
    ring.material = flat(this.scene, "selection", selectionColour);
    ring.isPickable = false;
    this.selection = ring;
  }

  /** The props drawn now (for the end-to-end tests): a home's furniture as it was last furnished. */
  get drawnProps(): readonly PropView[] {
    return this.props;
  }

  /** What a cat is drawn wearing (for the end-to-end tests), if it is in the room and drawn. */
  wearingOf(playerId: string): { fur: string; hat: string | undefined; collar: string | undefined } | undefined {
    return this.cats.get(playerId)?.model?.wearing;
  }

  /** The animation a cat is playing (for the end-to-end tests), if it is in the room and drawn. */
  animationOf(playerId: string): CatAnimation | undefined {
    return this.cats.get(playerId)?.model?.animation;
  }

  /** Sends the cat somewhere new: it gets up (a pose taken while it walks holds until the next walk). */
  walk(playerId: string, x: number, z: number): void {
    const cat = this.cats.get(playerId);
    if (cat) {
      this.sendTo(cat, x, z);
      cat.look.walk();
    }
  }

  /**
   * Resolves true once the cat is drawn standing at (x, z), or false if it is sent elsewhere first, leaves, or another
   * room shows (or the cat isn't here).
   */
  reached(playerId: string, x: number, z: number): Promise<boolean> {
    const cat = this.cats.get(playerId);
    if (!cat) {
      return Promise.resolve(false);
    }
    cat.arrival?.done(false);
    return new Promise((resolve) => {
      cat.arrival = { x, z, done: resolve };
    });
  }

  /** Where a spot on the floor is drawn, in CSS pixels from the canvas's top left; undefined until a room shows. */
  screenPoint(x: number, z: number): { x: number; y: number } | undefined {
    return this.project(new Vector3(x, 0, z));
  }

  /** Where a cat is drawn on the floor now, as it walks (not where it is walking to); undefined when it isn't here. */
  positionOf(playerId: string): { x: number; z: number } | undefined {
    const cat = this.cats.get(playerId);
    return cat && { x: cat.root.position.x, z: cat.root.position.z };
  }

  /**
   * Where the middle of a cat is drawn, as it walks, in CSS pixels from the canvas's top left; undefined when it isn't in
   * the room. For the end-to-end tests, which tap cats.
   */
  screenPositionOf(playerId: string): { x: number; y: number } | undefined {
    const cat = this.cats.get(playerId);
    return cat && this.project(cat.root.position.add(new Vector3(0, 0.5, 0)));
  }

  private project(spot: Vector3): { x: number; y: number } | undefined {
    const camera = this.scene.activeCamera;
    if (!camera) {
      return undefined;
    }
    // Worked out now: the camera keeps them from its last render, and a room just shown has had none.
    camera.getViewMatrix();
    camera.getProjectionMatrix();
    const width = this.engine.getRenderWidth();
    const height = this.engine.getRenderHeight();
    const point = Vector3.Project(
      spot, Matrix.Identity(), camera.getTransformationMatrix(), camera.viewport.toGlobal(width, height));
    // The canvas may draw at more pixels than it takes on the page.
    const scale = (this.engine.getRenderingCanvas()?.clientWidth ?? width) / width;
    return { x: point.x * scale, y: point.y * scale };
  }

  remove(playerId: string): void {
    const cat = this.cats.get(playerId);
    if (!cat) {
      return;
    }
    for (const control of [...(this.gui?.getDescendants() ?? [])]) {
      if (control.name === `name-${playerId}` || control.name === `bubble-${playerId}`) {
        control.dispose();
      }
    }
    cat.arrival?.done(false);
    cat.root.dispose();
    this.cats.delete(playerId);
  }

  speak(playerId: string, text: string): void {
    this.cats.get(playerId)?.look.say(text, performance.now());
  }

  /** The player muted this cat: the line over it goes at once. */
  hush(playerId: string): void {
    this.cats.get(playerId)?.look.hush();
  }

  /** What the bubble over a cat says now, if anything (for the end-to-end tests). */
  bubbleOf(playerId: string): string | undefined {
    return this.cats.get(playerId)?.look.bubbleAt(performance.now());
  }

  /**
   * Sit and sleep are poses the cat holds until it walks (asleep, with a "z z z" over it); wave raises a paw for a
   * moment; meow lifts the head with "meow!" over it.
   */
  emote(playerId: string, emote: Emote): void {
    this.cats.get(playerId)?.look.emote(emote, performance.now());
  }

  /**
   * Dresses the cat (its model, once drawn). Tapping any part of a cat, what it wears included, opens its card (the tap
   * decides whether the player's own cat takes it); its name's anchor never takes the pointer.
   */
  private dressOnScreen(cat: CatOnScreen, outfit: Outfit): void {
    cat.outfit = outfit;
    if (!cat.model) {
      return;
    }
    cat.model.dress(outfit);
    for (const mesh of cat.root.getChildMeshes(false)) {
      mesh.isPickable = mesh !== cat.anchor;
    }
  }

  /** Points the cat at a new spot; whoever waited for it to reach another is told it won't. */
  private sendTo(cat: CatOnScreen, x: number, z: number): void {
    cat.target.set(x, 0, z);
    if (cat.arrival && Math.hypot(cat.arrival.x - x, cat.arrival.z - z) > there) {
      cat.arrival.done(false);
      cat.arrival = undefined;
    }
  }

  /**
   * Draws a room exit as a doorway and mat by the back wall or an outdoor path and sign at the edge.
   * Returns the meshes that a click can use to take the exit.
   */
  private drawDoor(door: DoorView, room: Pick<RoomSnapshot, "roomId" | "width" | "depth">): AbstractMesh[] {
    const name = `door-${door.id}`;
    if (!hasDoorway(door, room.depth, room.roomId)) {
      return this.drawPath(door, room);
    }
    const matWidth = door.radius * 2;
    const matDepth = door.radius * 1.4;
    const edge = CreateBox(`${name}-mat-edge`, { width: matWidth + 0.16, depth: matDepth + 0.16, height: 0.02 }, this.scene);
    edge.position.set(door.x, 0.01, door.z);
    edge.material = flat(this.scene, `${name}-mat-edge`, ink);
    edge.isPickable = false;
    const mat = CreateBox(`${name}-mat`, { width: matWidth, depth: matDepth, height: 0.02 }, this.scene);
    mat.position.set(door.x, 0.03, door.z);
    mat.material = flat(this.scene, `${name}-mat`, doorMatColour);

    const wall = room.depth / 2;
    // Clear of the wall's own outline, which is drawn a little in front of it.
    const frame = CreateBox(`${name}-frame`, { width: 1.9, height: 2.7, depth: 0.04 }, this.scene);
    frame.position.set(door.x, 1.35, wall - 0.08);
    frame.material = flat(this.scene, `${name}-frame`, ink);
    const panel = CreateBox(`${name}-panel`, { width: 1.6, height: 2.5, depth: 0.04 }, this.scene);
    panel.position.set(door.x, 1.25, wall - 0.11);
    panel.material = flat(this.scene, `${name}-panel`, "#8a5a3c");

    // Within the doorway so the sign stays below the room bar on short screens.
    this.labelDoor(name, door.toRoomName, door.x, 2.15, wall - 0.16);
    return [mat, frame, panel];
  }

  private drawPath(door: DoorView, room: Pick<RoomSnapshot, "width" | "depth">): AbstractMesh[] {
    const name = `path-${door.id}`;
    const edge = pathEdge(door, room);
    const [dx, dz] = [edge.x - door.x, edge.z - door.z];
    const path = CreateBox(name, {
      width: dx === 0 ? 1.7 : Math.abs(dx) + 1.35,
      depth: dz === 0 ? 1.7 : Math.abs(dz) + 1.35,
      height: 0.03,
    }, this.scene);
    path.position.set(door.x + dx / 2, 0.025, door.z + dz / 2);
    path.material = flat(this.scene, name, pathColour);

    // A low signpost alongside the path, leaving the way through open.
    const [x, z] = dx === 0 ? [door.x + 1.25, door.z] : [door.x, door.z + 1.25];
    const post = CreateBox(`${name}-post`, { width: 0.17, height: 1.45, depth: 0.17 }, this.scene);
    post.position.set(x, 0.725, z);
    post.material = flat(this.scene, `${name}-post`, "#79543b");
    const board = CreateBox(`${name}-board`, { width: 1.75, height: 0.46, depth: 0.12 }, this.scene);
    board.position.set(x, 1.44, z);
    board.material = flat(this.scene, `${name}-board`, "#e8cf9f");
    this.labelDoor(name, door.toRoomName, x, 1.76, z);
    return [path, post, board];
  }

  /** The floor's dark edge, a little larger and just below it. Not renderOutline, whose pass, from far off (a narrow
   * screen backs the camera away), covered whatever lay flat on the floor, the pond. A room marked in the preview has a wide pink one, and its name. */
  private drawEdge(chunk: PlacedChunk, marked: boolean): void {
    const grow = marked ? 1.2 : 0.08;
    const rim = CreateGround("floor-edge", { width: chunk.width + grow, height: chunk.depth + grow }, this.scene);
    rim.position.set(chunk.x, -0.01, chunk.z);
    rim.material = flat(this.scene, "floor-edge", marked ? "#ff3b8b" : ink);
    rim.isPickable = false;
    if (marked) {
      this.nameRoom(chunk);
    }
  }

  private nameRoom(chunk: PlacedChunk): void {
    const anchor = CreateBox(`room-anchor-${chunk.roomId}`, { size: 0.01 }, this.scene);
    anchor.position.set(chunk.x, 0.3, chunk.z);
    anchor.isVisible = false;
    anchor.isPickable = false;
    const label = new TextBlock(`room-name-${chunk.roomId}`, chunk.name);
    label.color = "#ff3b8b";
    label.fontSize = 26;
    label.fontWeight = "bold";
    label.outlineColor = "white";
    label.outlineWidth = 5;
    this.gui?.addControl(label);
    label.linkWithMesh(anchor);
  }

  private labelDoor(name: string, title: string, x: number, y: number, z: number): void {
    const anchor = CreateBox(`${name}-anchor`, { size: 0.01 }, this.scene);
    anchor.position.set(x, y, z);
    anchor.isVisible = false;
    anchor.isPickable = false;
    const label = new TextBlock(`${name}-label`, title);
    label.color = ink;
    label.fontSize = 14;
    label.fontWeight = "bold";
    label.outlineColor = "white";
    label.outlineWidth = 3;
    this.gui?.addControl(label);
    label.linkWithMesh(anchor);
    this.doorLabels.push({ label, spot: new Vector3(x, y, z), length: title.length });
  }

  /** Door names that would draw over each other on screen are stacked, the left-most lowest. */
  private stackDoorLabels(): void {
    const shown = this.doorLabels.map((door) => ({ door, at: this.project(door.spot) }));
    const placed = shown.filter((item): item is { door: (typeof item)["door"]; at: { x: number; y: number } } => item.at !== undefined);
    // The labels sit on a texture the size of the render, and <project> answers in the canvas's size on the page.
    const canvasWidth = this.engine.getRenderWidth();
    const pageWidth = this.engine.getRenderingCanvas()?.clientWidth || canvasWidth;
    const kept = placed.map(({ door, at }) => {
      const width = door.length * labelCharWidth;
      const x = (at.x * canvasWidth) / pageWidth;
      const shift = canvasWidth > 0 ? keepOnScreen(x, width, canvasWidth) : 0;
      return { door, shift, name: { x: x + shift, y: (at.y * canvasWidth) / pageWidth, width } };
    });
    const levels = stackLevels(kept.map(({ name }) => name));
    kept.forEach(({ door, shift }, index) => {
      door.label.linkOffsetX = shift;
      door.label.linkOffsetY = -(levels[index] ?? 0) * labelLineHeight;
    });
  }

  private step(seconds: number): void {
    const now = performance.now();
    for (const [playerId, cat] of this.cats) {
      const offset = cat.target.subtract(cat.root.position);
      const distance = offset.length();
      const moving = distance > 0.01;
      if (moving) {
        const stride = Math.min(distance, walkSpeed * seconds);
        cat.root.position.addInPlace(offset.scale(stride / distance));
        cat.root.rotation.y = facing(offset.x, offset.z);
      }
      if (cat.arrival && Math.hypot(cat.root.position.x - cat.arrival.x, cat.root.position.z - cat.arrival.z) <= there) {
        const { done } = cat.arrival;
        cat.arrival = undefined;
        done(true);
      }
      this.drawLook(playerId, cat, moving, now);
    }
  }

  /** Brings the cat's animation and bubble in line with its look. */
  private drawLook(playerId: string, cat: CatOnScreen, moving: boolean, now: number): void {
    const { look } = cat;
    const doing = { moving, pose: look.pose, waving: look.wavingAt(now), meowing: false };
    const meow = cat.meow.next(look.meows, look.meowingAt(now), animationFor(doing));
    cat.model?.play(animationFor({ ...doing, meowing: meow.show }), meow.restart);

    const text = cat.look.bubbleAt(now);
    if (text !== cat.bubble?.text) {
      cat.bubble?.box.dispose();
      cat.bubble = text === undefined ? undefined : { box: this.bubble(playerId, cat, text), text };
    }
  }

  private bubble(playerId: string, cat: CatOnScreen, text: string): Rectangle {
    const box = new Rectangle(`bubble-${playerId}`);
    box.adaptWidthToChildren = true;
    box.adaptHeightToChildren = true;
    box.cornerRadius = 12;
    box.thickness = 2;
    box.color = ink;
    box.background = "white";
    const line = new TextBlock(`bubble-text-${playerId}`, text);
    line.resizeToFit = true;
    line.textWrapping = false;
    line.fontSize = 15;
    line.color = ink;
    line.paddingLeft = line.paddingRight = "10px";
    line.paddingTop = line.paddingBottom = "6px";
    box.addControl(line);
    this.gui?.addControl(box);
    box.linkWithMesh(cat.anchor);
    box.linkOffsetY = -42;
    return box;
  }
}

/** What a cat in a room wears. */
function outfitOf(cat: CatView): Outfit {
  return { fur: cat.furColor, hat: cat.hat, collar: cat.collar };
}

/** The player whose cat a picked mesh is part of (a cat's root carries the ID), if any. */
export function catPlayerIdOf(mesh: BabylonNode | null | undefined): string | undefined {
  for (let node = mesh; node; node = node.parent) {
    const id: unknown = (node.metadata as { catPlayerId?: unknown } | null)?.catPlayerId;
    if (typeof id === "string") {
      return id;
    }
  }
  return undefined;
}

/** Whether a spot on the ground lies on a placed room's floor (or its edge). */
function covers(chunk: PlacedChunk, x: number, z: number): boolean {
  return Math.abs(x - chunk.x) <= chunk.width / 2 && Math.abs(z - chunk.z) <= chunk.depth / 2;
}

/** Whether a spot lies on the floor of a placed room other than <chunk>: scenery there would stand on that room. */
export function onAnotherRoom(world: PlacedChunk[] | undefined, chunk: PlacedChunk, x: number, z: number): boolean {
  return world?.some((other) => other !== chunk && covers(other, x, z)) ?? false;
}

const labelCharWidth = 9;
const labelLineHeight = 18;

/** How far to move a name centred on <x> and <width> wide sideways so it fits a canvas <canvasWidth> wide (none if it is wider than the canvas). */
export function keepOnScreen(x: number, width: number, canvasWidth: number): number {
  if (width >= canvasWidth) {
    return 0;
  }
  const left = x - width / 2;
  const right = x + width / 2;
  if (left < 0) {
    return -left;
  }
  return right > canvasWidth ? canvasWidth - right : 0;
}

/** How many lines up each name must sit to clear the names already placed to its left: names are centred on (x, y) and as wide as <width>. */
export function stackLevels(names: { x: number; y: number; width: number }[]): number[] {
  const levelOf = new Map<{ x: number; y: number; width: number }, number>();
  const placed: { x: number; y: number; width: number }[] = [];
  for (const name of [...names].sort((a, b) => a.x - b.x)) {
    let level = 0;
    while (placed.some((other) => levelOf.get(other) === level
      && Math.abs(other.x - name.x) < (other.width + name.width) / 2 && Math.abs(other.y - name.y) < labelLineHeight)) {
      level++;
    }
    levelOf.set(name, level);
    placed.push(name);
  }
  return names.map((name) => levelOf.get(name) ?? 0);
}

/** How far a room's ground reaches past its edges, fading, in the open world: about as far as the camera sees. */
const groundBlend = 40;

/** The room whose ground lies under the whole open world: the one most of the placed rooms' area has, so no room's ground (the arrival room's street) runs on past the world's edge. */
function groundChunkOf(chunks: PlacedChunk[], arrival: PlacedChunk): PlacedChunk {
  const groups: { chunk: PlacedChunk; area: number }[] = [];
  for (const chunk of chunks) {
    const ground = lookOf(chunk.roomId, chunk.look).ground;
    if (!ground) {
      continue;
    }
    const group = groups.find((g) => sameGround(ground, lookOf(g.chunk.roomId, g.chunk.look).ground));
    if (group) {
      group.area += chunk.width * chunk.depth;
    } else {
      groups.push({ chunk, area: chunk.width * chunk.depth });
    }
  }
  return groups.reduce((best, g) => (g.area > best.area ? g : best), { chunk: arrival, area: 0 }).chunk;
}

/** Whether two grounds look the same: the same colour and painting. */
function sameGround(a: NonNullable<ReturnType<typeof lookOf>["ground"]>, b: ReturnType<typeof lookOf>["ground"]): boolean {
  return b !== undefined && a.colour === b.colour && a.paint === b.paint;
}

/** How much of a room's ground shows (0 to 255) at a spot of its patch: all of it over the room, fading to none at the patch's edge. (u, v) run 0 to 1 across the patch; (feather) is the fade's width in the same units. */
export function fadeAt(u: number, v: number, featherU: number, featherV: number): number {
  const edge = Math.min(Math.min(u, 1 - u) / featherU, Math.min(v, 1 - v) / featherV);
  return Math.round(255 * Math.max(0, Math.min(1, edge)));
}

function flat(scene: Scene, name: string, hex: string): StandardMaterial {
  const material = new StandardMaterial(name, scene);
  material.disableLighting = true;
  material.emissiveColor = Color3.FromHexString(hex);
  return material;
}

// The rooms' paintings, by what they show (<key> and size): each painter is seeded, so a painting is the same every
// time, and a room seen again reuses it rather than painting it anew. A few megabytes for the rooms there are.
const paintings = new Map<string, ICanvas>();

/**
 * A flat material painted by <paint> onto a canvas <size.width> by <size.height> pixels (<size.scale> to the metre),
 * shown as it is, unlit; once across its mesh, or <repeat> times each way. The painting is kept by <key> and painted
 * only the first time.
 */
function painted(scene: Scene, name: string, key: string, size: { width: number; height: number; scale: number },
  paint: (brush: Brush, scale: number) => void, repeat?: { u: number; v: number }): StandardMaterial {
  const sized = `${key}@${size.width}x${size.height}`;
  let canvas = paintings.get(sized);
  if (!canvas) {
    // Sides of powers of two (for WebGL1's mipmaps), painted stretched to fill them: the mesh shows it unstretched.
    const [width, height] = [powerOfTwo(size.width), powerOfTwo(size.height)];
    canvas = scene.getEngine().createCanvas(width, height);
    const brush = canvas.getContext("2d") as unknown as Brush;
    brush.scale(width / size.width, height / size.height);
    paint(brush, size.scale);
    paintings.set(sized, canvas);
  }
  // The texture draws from the kept canvas, and leaves it be when the room's scene is disposed.
  const texture = new DynamicTexture(name, canvas, scene, true);
  if (repeat) {
    // A dynamic texture clamps at its edges unless told otherwise.
    texture.wrapU = texture.wrapV = DynamicTexture.WRAP_ADDRESSMODE;
    texture.uScale = repeat.u;
    texture.vScale = repeat.v;
  }
  texture.update();
  const material = new StandardMaterial(name, scene);
  material.disableLighting = true;
  material.emissiveColor = Color3.White();
  material.diffuseTexture = texture;
  return material;
}

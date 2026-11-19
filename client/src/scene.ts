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
import { cameraPitch, cameraRadius, fieldOfView } from "./view";
import { animationFor, Cat, facing, loadCatModel, MeowClip, type CatAnimation, type Outfit } from "./cat";
import { hasDoorway } from "./doors";
import { lazily } from "./lazy";
import { appLog } from "./telemetry";
import { lookOf } from "./looks";
import {
  floorPixelsPerMetre, powerOfTwo, streetTile, textureSize, wallPixelsPerMetre, type Brush, type Floorplan,
} from "./paint";
import { catalogueStandKind } from "./catalogue";
import { Portraits } from "./portrait";
import { besideProp, buildParts, ink, propAt, propParts, type Part } from "./props";
import { CatLook } from "./look";
import catModelUrl from "./models/cat.glb?url";
import type { CatView, DoorView, Emote, OutfitView, PropView, RoomSnapshot } from "./types";

const walkSpeed = 4; // metres per second
const sky = Color4.FromHexString("#bfe3f2ff");
// Where the name and bubbles hang: just over the tips of the ears.
const anchorHeight = 1.4;
/** A door's mat: its own red, which nothing else in a room is (the layout test looks for it). */
export const doorMatColour = "#c8553d";
/** The ring round the furniture selected while decorating: the buttons' orange. */
const selectionColour = "#f0a04b";
/** How close (in metres) a cat drawn walking somewhere counts as there. */
const there = 0.05;
/** How tall a room's back wall is, in metres. */
const wallHeight = 4;

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
  private wall: Part | undefined;
  private decor: Mesh | undefined;
  private props: PropView[] = [];
  private selection: Mesh | undefined;
  /**
   * Decorating a home: a tap goes to the furniture or the floor under it (onEditTapped), never to a cat, a door or the
   * pond, and walks no one.
   */
  editing = false;
  // What a tap does, given everything along its ray (multiPick): set for the room showing.
  private tap: (picks: readonly PickingInfo[]) => void = () => {};
  /**
   * The player's own cat: a tap on it opens the player's own card, but only when nothing else a tap goes to is along its
   * ray (another cat, a door, the pond): a tap on a door just behind it still goes through the door.
   */
  playerId: string | undefined;

  /** Pictures of cats for their cards, taken off screen with this engine and the cat model. */
  readonly portraits: Portraits;

  /** <loadModel> reads the cat model into a room's scene (the tests hand in their own). */
  constructor(
    private readonly engine: AbstractEngine,
    private readonly loadModel: (scene: Scene) => Promise<AssetContainer> = catModelFor,
  ) {
    this.scene = new Scene(this.engine);
    this.scene.clearColor = sky;
    this.portraits = new Portraits(this.engine, this.loadModel);
    // One loop for the page; it draws whichever room is showing.
    this.engine.runRenderLoop(() => this.renderFrame());
  }

  /** Draws one frame of whatever is showing. Until a room shows there is no camera: the canvas keeps its sky. */
  renderFrame(): void {
    if (this.scene.activeCamera) {
      this.scene.render();
    }
  }

  onFloorClicked(handler: (x: number, z: number) => void): void {
    this.floorClicked = handler;
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

  /** A tap while decorating: where on the floor, under the furniture or the floor tapped. */
  onEditTapped(handler: (x: number, z: number) => void): void {
    this.editTapped = handler;
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

    // A fixed, angled camera: the player never steers it, like a painted backdrop.
    const camera = new ArcRotateCamera("camera", -Math.PI / 2, cameraPitch, 1, Vector3.Zero(), this.scene);
    camera.fov = fieldOfView;
    // Back far enough for the screen's shape, now and whenever it changes (a phone turned on its side).
    const fit = () => {
      camera.radius = cameraRadius(this.engine.getAspectRatio(camera), room.width, room.depth);
    };
    fit();
    this.engine.onResizeObservable.add(fit);
    this.scene.onDisposeObservable.add(() => this.engine.onResizeObservable.removeCallback(fit));
    camera.inputs.clear();

    const look = lookOf(room.roomId);
    this.scene.clearColor = Color4.FromHexString(`${look.sky}ff`);
    const plan: Floorplan = { width: room.width, depth: room.depth, pond: room.pond, doors: room.doors };
    const floor = CreateGround("floor", { width: room.width, height: room.depth }, this.scene);
    floor.material = look.paintFloor
      ? painted(this.scene, "floor", `${room.roomId}/floor/${JSON.stringify(plan)}`, textureSize(room.width, room.depth, floorPixelsPerMetre),
        (brush, scale) => look.paintFloor?.(brush, plan, scale))
      : flat(this.scene, "floor", look.floorColour);
    // The floor's dark edge: a little larger and just below it. Not renderOutline, whose pass, from far off (a narrow
    // screen backs the camera away), covered whatever lay flat on the floor, the pond.
    const rim = CreateGround("floor-edge", { width: room.width + 0.08, height: room.depth + 0.08 }, this.scene);
    rim.position.y = -0.01;
    rim.material = flat(this.scene, "floor-edge", ink);
    rim.isPickable = false;
    if (look.ground) {
      // The ground round the room, below the floor's edge: out past where the camera sees in front and to the sides,
      // and behind the backdrop (the sky shows over it).
      const [width, depth] = [400, 200 + room.depth / 2 + 40];
      const ground = CreateGround("ground", { width, height: depth }, this.scene);
      ground.position.set(0, -0.03, (room.depth / 2 + 40 - 200) / 2);
      const { colour, paint } = look.ground;
      ground.material = paint
        ? painted(this.scene, "ground", `${room.roomId}/ground`, textureSize(streetTile, streetTile, floorPixelsPerMetre), paint,
          { u: width / streetTile, v: depth / streetTile })
        : flat(this.scene, "ground", colour);
      ground.isPickable = false;
    }
    // The back wall, outlined like the props (and with them), its front painted: a plane just in front of it (the
    // doorways stand in front of that).
    const wall: Part = {
      shape: "box", width: room.width, height: wallHeight, depth: 0.3, at: [0, wallHeight / 2, room.depth / 2 + 0.15],
      colour: look.wallColour,
    };
    if (look.paintWall) {
      const front = CreatePlane("back-wall-front", { width: room.width, height: wallHeight }, this.scene);
      front.position.set(0, wallHeight / 2, room.depth / 2 - 0.01);
      // Painted round the doorways in it (the doors by the wall: the ones drawDoor draws a doorway for).
      const doorways = room.doors.filter((door) => hasDoorway(door, room.depth));
      front.material = painted(this.scene, "back-wall-front",
        `${room.roomId}/wall/${room.width}/${JSON.stringify(doorways)}`, textureSize(room.width, wallHeight, wallPixelsPerMetre),
        (brush, scale) => look.paintWall?.(brush, room.width, wallHeight, scale, doorways));
      front.isPickable = false;
    }
    // The wall and the props: one mesh, drawn in one go. Cats walk through props; a tap on one walks the cat beside it
    // (a tap on the wall does nothing). The backdrop is another, which a tap never tests: it does nothing there.
    this.wall = wall;
    this.props = room.props ?? [];
    this.decor = buildParts(this.scene, "decor", [wall, ...this.props.flatMap(propParts)]);
    const backdrop = buildParts(this.scene, "backdrop", look.backdrop(plan, wallHeight));
    if (backdrop) {
      backdrop.isPickable = false;
    }

    // The pond: a flat disc of water just above the floor. Clicking it fishes, rather than walking into it.
    let pond: AbstractMesh | undefined;
    if (room.pond) {
      // Two flat cylinders: the water, and a little larger and just below it, its dark edge, as the floor has. Not
      // renderOutline: on something this flat the outline pass painted over the whole of it.
      const edge = CreateCylinder("pond-edge", { diameter: room.pond.radius * 2 + 0.16, height: 0.02, tessellation: 48 }, this.scene);
      edge.position.set(room.pond.x, 0.01, room.pond.z);
      edge.material = flat(this.scene, "pond-edge", ink);
      edge.isPickable = false;
      pond = CreateCylinder("pond", { diameter: room.pond.radius * 2, height: 0.02, tessellation: 48 }, this.scene);
      pond.position.set(room.pond.x, 0.03, room.pond.z);
      pond.material = flat(this.scene, "pond", "#5fb3d9");
    }

    // The doors, each a mat on the floor (and, by the back wall, a doorway in it) that walks the cat through when clicked.
    const doors = new Map<AbstractMesh, DoorView>();
    for (const door of room.doors) {
      for (const part of this.drawDoor(door, room.depth)) {
        doors.set(part, door);
      }
    }

    // A tap goes to the first thing along its ray in this order, not to the nearest: another cat, a door, the pond, the
    // player's own cat, the catalogue stand, a prop (beside it), the floor. Props are tall and cats walk through them: a
    // cat among a table's chairs, or a door mat showing round a tree, still takes the tap; and the player's own cat
    // stands in front of the door it walks to.
    this.tap = (picks) => {
      const hits = picks.filter((pick) => pick.hit && pick.pickedMesh).sort((a, b) => a.distance - b.distance);
      const decor = this.decor;
      if (this.editing) {
        // The nearest furniture or floor along the ray (not the wall, which is in the same mesh): the spot on the floor
        // under it.
        const onFurniture = (point: Vector3) => this.props.some((p) => Math.hypot(point.x - p.x, point.z - p.z) <= p.radius);
        const spot = hits.map((pick) => pick.pickedMesh === floor || (pick.pickedMesh === decor && pick.pickedPoint && onFurniture(pick.pickedPoint))
          ? pick.pickedPoint
          : null).find((point) => point);
        if (spot) {
          this.editTapped(spot.x, spot.z);
        }
        return;
      }
      const catIds = hits.map((pick) => catPlayerIdOf(pick.pickedMesh));
      const catId = catIds.find((id) => id !== undefined && id !== this.playerId);
      const ownCat = this.playerId !== undefined && catIds.includes(this.playerId) ? this.playerId : undefined;
      const door = hits.map((pick) => (pick.pickedMesh ? doors.get(pick.pickedMesh) : undefined)).find((d) => d);
      const onProps = hits.filter((pick) => decor && pick.pickedMesh === decor && pick.pickedPoint);
      const ground = hits.find((pick) => pick.pickedMesh === floor)?.pickedPoint;
      // The stand, tapped, or the floor it stands on (it is small, to tap on a phone).
      const stand = [...onProps.map((pick) => pick.pickedPoint), ground]
        .map((point) => (point ? propAt(this.props, point.x, point.z) : undefined))
        .find((prop) => prop?.kind === catalogueStandKind);
      const beside = onProps
        .map((pick) => besideProp(this.props, room, pick.pickedPoint?.x ?? 0, pick.pickedPoint?.z ?? 0)).find((spot) => spot);
      if (catId !== undefined) {
        this.catClicked(catId);
      } else if (door) {
        this.doorClicked(door);
      } else if (pond && hits.some((pick) => pick.pickedMesh === pond)) {
        this.pondClicked();
      } else if (ownCat !== undefined) {
        this.catClicked(ownCat);
      } else if (stand) {
        this.standClicked(stand);
      } else if (beside) {
        this.floorClicked(beside.x, beside.z);
      } else if (ground) {
        this.floorClicked(ground.x, ground.z);
      }
    };
    this.scene.onPointerDown = () => this.tap(this.scene.multiPick(this.scene.pointerX, this.scene.pointerY) ?? []);
    this.scene.onBeforeRenderObservable.add(() => this.step(this.engine.getDeltaTime() / 1000));

    for (const cat of room.cats) {
      this.place(cat);
    }
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

  /** A home's furniture changed: the room's props are drawn again as <props>, with the back wall (one mesh). */
  furnish(props: PropView[]): void {
    if (!this.wall) {
      return;
    }
    this.decor?.dispose(false, true);
    this.props = props;
    this.decor = buildParts(this.scene, "decor", [this.wall, ...props.flatMap(propParts)]);
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
   * Draws a door, flat and outlined like the rest of the room: a mat on the floor over its spot, with a dark edge as the
   * pond has (not renderOutline, which painted over flat floor things), and, when it is by the back wall, a doorway in
   * the wall behind it with the name of the room it leads to over it. Returns the parts a click goes through.
   */
  private drawDoor(door: DoorView, depth: number): AbstractMesh[] {
    const name = `door-${door.id}`;
    const matWidth = door.radius * 2;
    const matDepth = door.radius * 1.4;
    const edge = CreateBox(`${name}-mat-edge`, { width: matWidth + 0.16, depth: matDepth + 0.16, height: 0.02 }, this.scene);
    edge.position.set(door.x, 0.01, door.z);
    edge.material = flat(this.scene, `${name}-mat-edge`, ink);
    edge.isPickable = false;
    const mat = CreateBox(`${name}-mat`, { width: matWidth, depth: matDepth, height: 0.02 }, this.scene);
    mat.position.set(door.x, 0.03, door.z);
    mat.material = flat(this.scene, `${name}-mat`, doorMatColour);

    // Only a door near the back wall has a doorway in it; one further out is just its mat.
    const wall = depth / 2;
    if (!hasDoorway(door, depth)) {
      return [mat];
    }
    // Clear of the wall's own outline, which is drawn a little in front of it.
    const frame = CreateBox(`${name}-frame`, { width: 1.9, height: 2.7, depth: 0.04 }, this.scene);
    frame.position.set(door.x, 1.35, wall - 0.08);
    frame.material = flat(this.scene, `${name}-frame`, ink);
    const panel = CreateBox(`${name}-panel`, { width: 1.6, height: 2.5, depth: 0.04 }, this.scene);
    panel.position.set(door.x, 1.25, wall - 0.11);
    panel.material = flat(this.scene, `${name}-panel`, "#8a5a3c");

    const anchor = CreateBox(`${name}-anchor`, { size: 0.01 }, this.scene);
    anchor.position.set(door.x, 3.1, wall - 0.11);
    anchor.isVisible = false;
    anchor.isPickable = false;
    const label = new TextBlock(`${name}-label`, door.toRoomName);
    label.color = ink;
    label.fontSize = 14;
    label.fontWeight = "bold";
    label.outlineColor = "white";
    label.outlineWidth = 3;
    this.gui?.addControl(label);
    label.linkWithMesh(anchor);
    return [mat, frame, panel];
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

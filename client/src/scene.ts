// Babylon by module, not from its index, so the page loads only what it uses.
import { ArcRotateCamera } from "@babylonjs/core/Cameras/arcRotateCamera";
import type { AbstractEngine } from "@babylonjs/core/Engines/abstractEngine";
import { StandardMaterial } from "@babylonjs/core/Materials/standardMaterial";
import { Color3, Color4 } from "@babylonjs/core/Maths/math.color";
import { Matrix, Vector3 } from "@babylonjs/core/Maths/math.vector";
import type { AbstractMesh } from "@babylonjs/core/Meshes/abstractMesh";
import { CreateBox } from "@babylonjs/core/Meshes/Builders/boxBuilder";
import { CreateCylinder } from "@babylonjs/core/Meshes/Builders/cylinderBuilder";
import { CreateGround } from "@babylonjs/core/Meshes/Builders/groundBuilder";
import type { TransformNode } from "@babylonjs/core/Meshes/transformNode";
import { Scene } from "@babylonjs/core/scene";
// For their side effects: picking what a pointer touches, and the meshes' drawn outlines (renderOutline).
import "@babylonjs/core/Culling/ray";
import "@babylonjs/core/Rendering/outlineRenderer";
import { AdvancedDynamicTexture } from "@babylonjs/gui/2D/advancedDynamicTexture";
import { Rectangle } from "@babylonjs/gui/2D/controls/rectangle";
import { TextBlock } from "@babylonjs/gui/2D/controls/textBlock";
import { cameraPitch, cameraRadius, fieldOfView } from "./view";
import { buildCat } from "./cat";
import { poseScales } from "./emotes";
import { CatLook } from "./look";
import type { CatView, DoorView, Emote, RoomSnapshot } from "./types";

const walkSpeed = 4; // metres per second
const sky = Color4.FromHexString("#bfe3f2ff");
/** A door's mat: its own red, which nothing else in a room is (the layout test looks for it). */
export const doorMatColour = "#c8553d";
/** How close (in metres) a cat drawn walking somewhere counts as there. */
const there = 0.05;

interface CatOnScreen {
  root: TransformNode;
  // The parts a pose squashes.
  body: TransformNode;
  anchor: AbstractMesh;
  target: Vector3;
  look: CatLook;
  bubble?: { box: Rectangle; text: string };
  // Hidden until the cat waves.
  paw: AbstractMesh;
  // Someone waiting for the cat to be drawn standing at a spot (a walk to a door), told true when it is, false if it
  // is sent elsewhere first.
  arrival?: { x: number; z: number; done: (arrived: boolean) => void };
}

/** Draws one room at a time and walks the cats in it. Knows nothing about the network. */
export class RoomScene {
  private scene: Scene;
  private gui: AdvancedDynamicTexture | undefined;
  private readonly cats = new Map<string, CatOnScreen>();
  private floorClicked: (x: number, z: number) => void = () => {};
  private pondClicked: () => void = () => {};
  private doorClicked: (door: DoorView) => void = () => {};

  constructor(private readonly engine: AbstractEngine) {
    this.scene = new Scene(this.engine);
    this.scene.clearColor = sky;
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

  /** Replaces whatever room was showing. */
  show(room: RoomSnapshot): void {
    for (const cat of this.cats.values()) {
      cat.arrival?.done(false);
    }
    this.cats.clear();
    this.scene.dispose();
    this.scene = new Scene(this.engine);
    this.scene.clearColor = sky;
    this.gui = AdvancedDynamicTexture.CreateFullscreenUI("ui", true, this.scene);

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

    const floor = CreateGround("floor", { width: room.width, height: room.depth }, this.scene);
    floor.material = flat(this.scene, "floor", room.roomId === "cafe" ? "#d9b38c" : "#9fd38a");
    // The floor's dark edge: a little larger and just below it. Not renderOutline, whose pass, from far off (a narrow
    // screen backs the camera away), covered whatever lay flat on the floor, the pond.
    const rim = CreateGround("floor-edge", { width: room.width + 0.08, height: room.depth + 0.08 }, this.scene);
    rim.position.y = -0.01;
    rim.material = flat(this.scene, "floor-edge", "#2a2230");
    rim.isPickable = false;
    const back = CreateBox("back-wall", { width: room.width, height: 4, depth: 0.3 }, this.scene);
    back.position.set(0, 2, room.depth / 2 + 0.15);
    back.material = flat(this.scene, "wall", room.roomId === "cafe" ? "#f2d6c2" : "#f4efe4");
    back.isPickable = false;
    back.renderOutline = true;
    back.outlineWidth = 0.04;
    back.outlineColor = Color3.FromHexString("#2a2230");

    // The pond: a flat disc of water just above the floor. Clicking it fishes, rather than walking into it.
    let pond: AbstractMesh | undefined;
    if (room.pond) {
      // Two flat cylinders: the water, and a little larger and just below it, its dark edge, as the floor has. Not
      // renderOutline: on something this flat the outline pass painted over the whole of it.
      const edge = CreateCylinder("pond-edge", { diameter: room.pond.radius * 2 + 0.16, height: 0.02, tessellation: 48 }, this.scene);
      edge.position.set(room.pond.x, 0.01, room.pond.z);
      edge.material = flat(this.scene, "pond-edge", "#2a2230");
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

    this.scene.onPointerDown = (_event, pick) => {
      const door = pick.hit && pick.pickedMesh ? doors.get(pick.pickedMesh) : undefined;
      if (door) {
        this.doorClicked(door);
      } else if (pick.hit && pond && pick.pickedMesh === pond) {
        this.pondClicked();
      } else if (pick.hit && pick.pickedMesh === floor && pick.pickedPoint) {
        this.floorClicked(pick.pickedPoint.x, pick.pickedPoint.z);
      }
    };
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
      return;
    }

    // The anchor (name, bubbles) and the paw hang off the root, which poses don't squash.
    const { root, body, paw } = buildCat(this.scene, `cat-${cat.playerId}`, cat.furColor);
    root.position.set(cat.x, 0, cat.z);
    const anchor = CreateBox(`anchor-${cat.playerId}`, { size: 0.01 }, this.scene);
    anchor.parent = root;
    anchor.position.y = 1.7;
    anchor.isVisible = false;
    anchor.isPickable = false;

    const label = new TextBlock(`name-${cat.playerId}`, cat.displayName);
    label.color = "#2a2230";
    label.fontSize = 14;
    label.fontWeight = "bold";
    label.outlineColor = "white";
    label.outlineWidth = 3;
    this.gui?.addControl(label);
    label.linkWithMesh(anchor);
    label.linkOffsetY = -6;

    this.cats.set(cat.playerId, { root, body, paw, anchor, target: new Vector3(cat.x, 0, cat.z), look: new CatLook(cat.pose) });
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
    const camera = this.scene.activeCamera;
    if (!camera) {
      return undefined;
    }
    const width = this.engine.getRenderWidth();
    const height = this.engine.getRenderHeight();
    const point = Vector3.Project(
      new Vector3(x, 0, z), Matrix.Identity(), camera.getTransformationMatrix(), camera.viewport.toGlobal(width, height));
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

  /**
   * Stand-ins until the cat is a modelled, animated one: sit and sleep squash the cat's body into a pose it holds until
   * it walks (asleep, with a "z z z" over it), wave raises a paw for a moment, and meow is a speech bubble.
   */
  emote(playerId: string, emote: Emote): void {
    this.cats.get(playerId)?.look.emote(emote, performance.now());
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
    const ink = "#2a2230";
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
    if (wall - door.z > 2) {
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
      if (distance > 0.01) {
        const stride = Math.min(distance, walkSpeed * seconds);
        cat.root.position.addInPlace(offset.scale(stride / distance));
        cat.root.rotation.y = Math.atan2(offset.x, offset.z);
      }
      if (cat.arrival && Math.hypot(cat.root.position.x - cat.arrival.x, cat.root.position.z - cat.arrival.z) <= there) {
        const { done } = cat.arrival;
        cat.arrival = undefined;
        done(true);
      }
      this.drawLook(playerId, cat, now);
    }
  }

  /** Brings the cat's pose, bubble and paw in line with its look. */
  private drawLook(playerId: string, cat: CatOnScreen, now: number): void {
    const [x, y, z] = cat.look.pose ? poseScales[cat.look.pose] : [1, 1, 1];
    cat.body.scaling.set(x, y, z);

    const text = cat.look.bubbleAt(now);
    if (text !== cat.bubble?.text) {
      cat.bubble?.box.dispose();
      cat.bubble = text === undefined ? undefined : { box: this.bubble(playerId, cat, text), text };
    }

    const waving = cat.look.wavingAt(now);
    cat.paw.setEnabled(waving);
    if (waving) {
      // Beside the head, however low the pose has it, waving side to side.
      cat.paw.position.set(0.42 + 0.08 * Math.sin(now / 70), 1.05 * y + 0.15, 0.55 * z);
    }
  }

  private bubble(playerId: string, cat: CatOnScreen, text: string): Rectangle {
    const box = new Rectangle(`bubble-${playerId}`);
    box.adaptWidthToChildren = true;
    box.adaptHeightToChildren = true;
    box.cornerRadius = 12;
    box.thickness = 2;
    box.color = "#2a2230";
    box.background = "white";
    const line = new TextBlock(`bubble-text-${playerId}`, text);
    line.resizeToFit = true;
    line.textWrapping = false;
    line.fontSize = 15;
    line.color = "#2a2230";
    line.paddingLeft = line.paddingRight = "10px";
    line.paddingTop = line.paddingBottom = "6px";
    box.addControl(line);
    this.gui?.addControl(box);
    box.linkWithMesh(cat.anchor);
    box.linkOffsetY = -42;
    return box;
  }
}

function flat(scene: Scene, name: string, hex: string): StandardMaterial {
  const material = new StandardMaterial(name, scene);
  material.disableLighting = true;
  material.emissiveColor = Color3.FromHexString(hex);
  return material;
}

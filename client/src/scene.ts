// Babylon by module, not from its index, so the page loads only what it uses.
import { ArcRotateCamera } from "@babylonjs/core/Cameras/arcRotateCamera";
import type { AssetContainer } from "@babylonjs/core/assetContainer";
import type { AbstractEngine } from "@babylonjs/core/Engines/abstractEngine";
import { StandardMaterial } from "@babylonjs/core/Materials/standardMaterial";
import { Color3, Color4 } from "@babylonjs/core/Maths/math.color";
import { Vector3 } from "@babylonjs/core/Maths/math.vector";
import type { AbstractMesh } from "@babylonjs/core/Meshes/abstractMesh";
import { CreateBox } from "@babylonjs/core/Meshes/Builders/boxBuilder";
import { CreateCylinder } from "@babylonjs/core/Meshes/Builders/cylinderBuilder";
import { CreateGround } from "@babylonjs/core/Meshes/Builders/groundBuilder";
import { TransformNode } from "@babylonjs/core/Meshes/transformNode";
import { Scene } from "@babylonjs/core/scene";
// For their side effects: picking what a pointer touches, and the meshes' drawn outlines (renderOutline).
import "@babylonjs/core/Culling/ray";
import "@babylonjs/core/Rendering/outlineRenderer";
import { AdvancedDynamicTexture } from "@babylonjs/gui/2D/advancedDynamicTexture";
import { Rectangle } from "@babylonjs/gui/2D/controls/rectangle";
import { TextBlock } from "@babylonjs/gui/2D/controls/textBlock";
import { cameraPitch, cameraRadius, fieldOfView } from "./view";
import { animationFor, Cat, facing, loadCatModel, MeowClip, type CatAnimation } from "./cat";
import { lazily } from "./lazy";
import { CatLook } from "./look";
import catModelUrl from "./models/cat.glb?url";
import type { CatView, Emote, RoomSnapshot } from "./types";

const walkSpeed = 4; // metres per second
const sky = Color4.FromHexString("#bfe3f2ff");
// Where the name and bubbles hang: just over the tips of the ears.
const anchorHeight = 1.4;

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
  meow: MeowClip;
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

  /** <loadModel> reads the cat model into a room's scene (the tests hand in their own). */
  constructor(
    private readonly engine: AbstractEngine,
    private readonly loadModel: (scene: Scene) => Promise<AssetContainer> = catModelFor,
  ) {
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

  /** Replaces whatever room was showing. */
  show(room: RoomSnapshot): void {
    this.cats.clear();
    this.scene.dispose();
    this.scene = new Scene(this.engine);
    this.scene.clearColor = sky;
    this.gui = AdvancedDynamicTexture.CreateFullscreenUI("ui", true, this.scene);
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
          console.error("The cat model didn't load", error);
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

    this.scene.onPointerDown = (_event, pick) => {
      if (pick.hit && pond && pick.pickedMesh === pond) {
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
      existing.target.set(cat.x, 0, cat.z);
      existing.look.pose = cat.pose ?? undefined;
      return;
    }

    // The anchor (name, bubbles) hangs off the root, above whatever the model is doing.
    const name = `cat-${cat.playerId}`;
    const root = new TransformNode(name, this.scene);
    root.position.set(cat.x, 0, cat.z);
    // Facing the camera until it walks somewhere.
    root.rotation.y = Math.PI;
    const anchor = CreateBox(`anchor-${cat.playerId}`, { size: 0.01 }, this.scene);
    anchor.parent = root;
    anchor.position.y = anchorHeight;
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

    const look = new CatLook(cat.pose);
    const shown: CatOnScreen = { root, anchor, target: new Vector3(cat.x, 0, cat.z), look, meow: new MeowClip() };
    this.cats.set(cat.playerId, shown);
    const scene = this.scene;
    void this.model.then((model) => {
      // Not for a cat that left, or a room that's gone, meanwhile.
      if (model && this.cats.get(cat.playerId) === shown && this.scene === scene && !root.isDisposed()) {
        try {
          shown.model = new Cat(scene, model, root, name, cat.furColor);
        } catch (error) {
          // That cat goes undrawn; its name and bubbles still show.
          console.error("A cat couldn't be drawn", error);
        }
      }
    });
  }

  /** The animation a cat is playing (for the end-to-end tests), if it is in the room and drawn. */
  animationOf(playerId: string): CatAnimation | undefined {
    return this.cats.get(playerId)?.model?.animation;
  }

  /** Sends the cat somewhere new: it gets up (a pose taken while it walks holds until the next walk). */
  walk(playerId: string, x: number, z: number): void {
    const cat = this.cats.get(playerId);
    cat?.target.set(x, 0, z);
    cat?.look.walk();
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
    cat.root.dispose();
    this.cats.delete(playerId);
  }

  speak(playerId: string, text: string): void {
    this.cats.get(playerId)?.look.say(text, performance.now());
  }

  /**
   * Sit and sleep are poses the cat holds until it walks (asleep, with a "z z z" over it); wave raises a paw for a
   * moment; meow lifts the head with "meow!" over it.
   */
  emote(playerId: string, emote: Emote): void {
    this.cats.get(playerId)?.look.emote(emote, performance.now());
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

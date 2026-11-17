// Babylon by module, not from its index, so the page loads only what it uses.
import { ArcRotateCamera } from "@babylonjs/core/Cameras/arcRotateCamera";
import type { AbstractEngine } from "@babylonjs/core/Engines/abstractEngine";
import { StandardMaterial } from "@babylonjs/core/Materials/standardMaterial";
import { Color3, Color4 } from "@babylonjs/core/Maths/math.color";
import { Vector3 } from "@babylonjs/core/Maths/math.vector";
import type { AbstractMesh } from "@babylonjs/core/Meshes/abstractMesh";
import { CreateBox } from "@babylonjs/core/Meshes/Builders/boxBuilder";
import { CreateDisc } from "@babylonjs/core/Meshes/Builders/discBuilder";
import { CreateGround } from "@babylonjs/core/Meshes/Builders/groundBuilder";
import type { TransformNode } from "@babylonjs/core/Meshes/transformNode";
import { Scene } from "@babylonjs/core/scene";
// For their side effects: picking what a pointer touches, and the meshes' drawn outlines (renderOutline).
import "@babylonjs/core/Culling/ray";
import "@babylonjs/core/Rendering/outlineRenderer";
import { AdvancedDynamicTexture } from "@babylonjs/gui/2D/advancedDynamicTexture";
import { Rectangle } from "@babylonjs/gui/2D/controls/rectangle";
import { TextBlock } from "@babylonjs/gui/2D/controls/textBlock";
import { cameraRadius, fieldOfView } from "./view";
import { buildCat } from "./cat";
import { poseScales } from "./emotes";
import { CatLook } from "./look";
import type { CatView, Emote, RoomSnapshot } from "./types";

const walkSpeed = 4; // metres per second
const sky = Color4.FromHexString("#bfe3f2ff");

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
}

/** Draws one room at a time and walks the cats in it. Knows nothing about the network. */
export class RoomScene {
  private scene: Scene;
  private gui: AdvancedDynamicTexture | undefined;
  private readonly cats = new Map<string, CatOnScreen>();
  private floorClicked: (x: number, z: number) => void = () => {};
  private pondClicked: () => void = () => {};

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

  /** Replaces whatever room was showing. */
  show(room: RoomSnapshot): void {
    this.cats.clear();
    this.scene.dispose();
    this.scene = new Scene(this.engine);
    this.scene.clearColor = sky;
    this.gui = AdvancedDynamicTexture.CreateFullscreenUI("ui", true, this.scene);

    // A fixed, angled camera: the player never steers it, like a painted backdrop.
    const camera = new ArcRotateCamera("camera", -Math.PI / 2, 0.95, 1, Vector3.Zero(), this.scene);
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
    const back = CreateBox("back-wall", { width: room.width, height: 4, depth: 0.3 }, this.scene);
    back.position.set(0, 2, room.depth / 2 + 0.15);
    back.material = flat(this.scene, "wall", room.roomId === "cafe" ? "#f2d6c2" : "#f4efe4");
    back.isPickable = false;
    for (const mesh of [floor, back]) {
      mesh.renderOutline = true;
      mesh.outlineWidth = 0.04;
      mesh.outlineColor = Color3.FromHexString("#2a2230");
    }

    // The pond: a flat disc of water just above the floor. Clicking it fishes, rather than walking into it.
    let pond: AbstractMesh | undefined;
    if (room.pond) {
      pond = CreateDisc("pond", { radius: room.pond.radius, tessellation: 48 }, this.scene);
      // Turned to face up: facing down, only its back shows, which the outline pass paints in the outline colour.
      pond.rotation.x = -Math.PI / 2;
      pond.position.set(room.pond.x, 0.02, room.pond.z);
      pond.material = flat(this.scene, "pond", "#5fb3d9");
      pond.renderOutline = true;
      pond.outlineWidth = 0.04;
      pond.outlineColor = Color3.FromHexString("#2a2230");
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
   * Stand-ins until the cat is a modelled, animated one: sit and sleep squash the cat's body into a pose it holds until
   * it walks (asleep, with a "z z z" over it), wave raises a paw for a moment, and meow is a speech bubble.
   */
  emote(playerId: string, emote: Emote): void {
    this.cats.get(playerId)?.look.emote(emote, performance.now());
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

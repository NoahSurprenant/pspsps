import {
  ArcRotateCamera, Color3, Color4, MeshBuilder, Scene, StandardMaterial, Vector3, type AbstractEngine, type AbstractMesh,
  type TransformNode,
} from "@babylonjs/core";
import { AdvancedDynamicTexture, Rectangle, TextBlock } from "@babylonjs/gui";
import { buildCat } from "./cat";
import type { CatView, RoomSnapshot } from "./types";

const walkSpeed = 4; // metres per second
const bubbleSeconds = 6;
const sky = Color4.FromHexString("#bfe3f2ff");

interface CatOnScreen {
  root: TransformNode;
  anchor: AbstractMesh;
  target: Vector3;
  bubble?: { box: Rectangle; until: number };
}

/** Draws one room at a time and walks the cats in it. Knows nothing about the network. */
export class RoomScene {
  private scene: Scene;
  private gui: AdvancedDynamicTexture | undefined;
  private readonly cats = new Map<string, CatOnScreen>();
  private floorClicked: (x: number, z: number) => void = () => {};

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

  /** Replaces whatever room was showing. */
  show(room: RoomSnapshot): void {
    this.cats.clear();
    this.scene.dispose();
    this.scene = new Scene(this.engine);
    this.scene.clearColor = sky;
    this.gui = AdvancedDynamicTexture.CreateFullscreenUI("ui", true, this.scene);

    // A fixed, angled camera: the player never steers it, like a painted backdrop.
    const camera = new ArcRotateCamera("camera", -Math.PI / 2, 0.95, Math.max(room.width, room.depth) * 1.15, Vector3.Zero(), this.scene);
    camera.fov = 0.7;
    camera.inputs.clear();

    const floor = MeshBuilder.CreateGround("floor", { width: room.width, height: room.depth }, this.scene);
    floor.material = flat(this.scene, "floor", room.roomId === "cafe" ? "#d9b38c" : "#9fd38a");
    const back = MeshBuilder.CreateBox("back-wall", { width: room.width, height: 4, depth: 0.3 }, this.scene);
    back.position.set(0, 2, room.depth / 2 + 0.15);
    back.material = flat(this.scene, "wall", room.roomId === "cafe" ? "#f2d6c2" : "#f4efe4");
    back.isPickable = false;
    for (const mesh of [floor, back]) {
      mesh.renderOutline = true;
      mesh.outlineWidth = 0.04;
      mesh.outlineColor = Color3.FromHexString("#2a2230");
    }

    this.scene.onPointerDown = (_event, pick) => {
      if (pick.hit && pick.pickedMesh === floor && pick.pickedPoint) {
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
      return;
    }

    const root = buildCat(this.scene, `cat-${cat.playerId}`, cat.furColor);
    root.position.set(cat.x, 0, cat.z);
    const anchor = MeshBuilder.CreateBox(`anchor-${cat.playerId}`, { size: 0.01 }, this.scene);
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

    this.cats.set(cat.playerId, { root, anchor, target: new Vector3(cat.x, 0, cat.z) });
  }

  walk(playerId: string, x: number, z: number): void {
    this.cats.get(playerId)?.target.set(x, 0, z);
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
    const cat = this.cats.get(playerId);
    if (!cat) {
      return;
    }
    cat.bubble?.box.dispose();

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
    cat.bubble = { box, until: performance.now() + bubbleSeconds * 1000 };
  }

  private step(seconds: number): void {
    const now = performance.now();
    for (const cat of this.cats.values()) {
      const offset = cat.target.subtract(cat.root.position);
      const distance = offset.length();
      if (distance > 0.01) {
        const stride = Math.min(distance, walkSpeed * seconds);
        cat.root.position.addInPlace(offset.scale(stride / distance));
        cat.root.rotation.y = Math.atan2(offset.x, offset.z);
      }
      if (cat.bubble && now > cat.bubble.until) {
        cat.bubble.box.dispose();
        cat.bubble = undefined;
      }
    }
  }
}

function flat(scene: Scene, name: string, hex: string): StandardMaterial {
  const material = new StandardMaterial(name, scene);
  material.disableLighting = true;
  material.emissiveColor = Color3.FromHexString(hex);
  return material;
}

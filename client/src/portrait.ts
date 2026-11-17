// Pictures of cats for their player cards: the room's cat model, dressed, facing the viewer, drawn once off screen into
// an image. Nothing is downloaded (the model's bytes are the room's), and nothing keeps drawing: a picture is a still,
// taken once per outfit and kept, in a scene of its own on the room's engine that is gone as soon as it is taken.
import { ArcRotateCamera } from "@babylonjs/core/Cameras/arcRotateCamera";
import type { AssetContainer } from "@babylonjs/core/assetContainer";
import type { AbstractEngine } from "@babylonjs/core/Engines/abstractEngine";
import { RenderTargetTexture } from "@babylonjs/core/Materials/Textures/renderTargetTexture";
import { Color4 } from "@babylonjs/core/Maths/math.color";
import { Vector3 } from "@babylonjs/core/Maths/math.vector";
import { TransformNode } from "@babylonjs/core/Meshes/transformNode";
import { Scene } from "@babylonjs/core/scene";
import { Cat, type Outfit } from "./cat";

/** How many pixels a side a picture is (shown at half that on the card: sharp on a phone's screen). */
export const pictureSize = 320;

/** How many pictures are kept, the latest: a card opened again shows at once. */
const kept = 24;

/** What takes the picture: draws <scene> through <camera> into a square <size> pixels a side, as an image's URL. */
export type Snap = (scene: Scene, camera: ArcRotateCamera, size: number) => Promise<string>;

/** An outfit as a key: the same outfit, the same picture. */
export function outfitKey(outfit: Outfit): string {
  return `${outfit.fur}/${outfit.hat ?? ""}/${outfit.collar ?? ""}`;
}

/** The pictures of cats, by outfit, taken one at a time. */
export class Portraits {
  private readonly taken = new Map<string, string>();
  private queue: Promise<unknown> = Promise.resolve();
  /** The cat the last picture was taken of, as it was dressed (for the tests). */
  lastSitter: Cat | undefined;

  constructor(
    private readonly engine: AbstractEngine,
    private readonly loadModel: (scene: Scene) => Promise<AssetContainer>,
    private readonly snap: Snap = drawToImage,
  ) {}

  /** The picture of a cat in <outfit>, if it has been taken. */
  cached(outfit: Outfit): string | undefined {
    return this.taken.get(outfitKey(outfit));
  }

  /** A picture of a cat dressed in <outfit>, facing the viewer: the one taken already, or taken now (one at a time). */
  picture(outfit: Outfit): Promise<string> {
    const already = this.cached(outfit);
    if (already) {
      return Promise.resolve(already);
    }
    const next = this.queue.then(() => this.cached(outfit) ?? this.take(outfit));
    // A picture that couldn't be taken doesn't stop the next.
    this.queue = next.catch(() => undefined);
    return next;
  }

  private async take(outfit: Outfit): Promise<string> {
    // Virtual: not one of the engine's scenes, so nothing else (the render loop, the room) ever sees it.
    const scene = new Scene(this.engine, { virtual: true });
    try {
      scene.clearColor = new Color4(0, 0, 0, 0);
      // Level with the cat's face, a little above, and back far enough for a top hat's crown and its paws.
      const camera = new ArcRotateCamera("portrait", -Math.PI / 2, 1.38, 2.7, new Vector3(0, 0.62, 0), scene);
      camera.fov = 0.62;
      const model = await this.loadModel(scene);
      const root = new TransformNode("sitter", scene);
      // The model looks along +z; the camera is in front, at -z.
      root.rotation.y = Math.PI;
      const cat = new Cat(scene, model, root, "sitter", outfit.fur);
      cat.dress(outfit);
      this.lastSitter = cat;
      const url = await this.snap(scene, camera, pictureSize);
      this.taken.set(outfitKey(outfit), url);
      if (this.taken.size > kept) {
        const oldest = this.taken.keys().next().value;
        if (oldest !== undefined) {
          this.taken.delete(oldest);
        }
      }
      return url;
    } finally {
      scene.dispose();
    }
  }
}

/**
 * Draws <scene> once into a texture of its own, square, then reads it back into a PNG. The camera's shape is the
 * canvas's unless told otherwise: square, just while it draws (synchronously, so the room never draws meanwhile).
 */
async function drawToImage(scene: Scene, camera: ArcRotateCamera, size: number): Promise<string> {
  const engine = scene.getEngine();
  const target = new RenderTargetTexture("portrait", { width: size, height: size }, scene, false, false, undefined, false,
    undefined, undefined, true);
  try {
    scene.activeCamera = camera;
    await scene.whenReadyAsync();
    const [width, height] = [engine.getRenderWidth, engine.getRenderHeight];
    engine.getRenderWidth = () => size;
    engine.getRenderHeight = () => size;
    camera.outputRenderTarget = target;
    try {
      scene.render();
    } finally {
      engine.getRenderWidth = width;
      engine.getRenderHeight = height;
      camera.outputRenderTarget = null;
    }
    const pixels = await target.readPixels();
    if (!pixels) {
      throw new Error("The cat's picture couldn't be read");
    }
    return toPng(new Uint8Array(pixels.buffer, pixels.byteOffset, pixels.byteLength), size);
  } finally {
    target.dispose();
  }
}

/** RGBA pixels, bottom row first (as WebGL reads them), as a PNG's data URL. */
function toPng(pixels: Uint8Array, size: number): string {
  const canvas = document.createElement("canvas");
  canvas.width = canvas.height = size;
  const context = canvas.getContext("2d");
  if (!context) {
    throw new Error("No canvas to make the cat's picture with");
  }
  const image = context.createImageData(size, size);
  const row = size * 4;
  for (let y = 0; y < size; y++) {
    image.data.set(pixels.subarray((size - 1 - y) * row, (size - y) * row), y * row);
  }
  context.putImageData(image, 0, 0);
  return canvas.toDataURL("image/png");
}

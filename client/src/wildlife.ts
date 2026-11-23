import type { Scene } from "@babylonjs/core/scene";
import { buildParts, palette, type Part } from "./props";
import { wildlifeRecipe, type WildlifeMotion } from "./recipes";
import type { RoomSnapshot } from "./types";

/** A pair of fish under the blue shallows or a butterfly visiting the courtyard flowers. Both let taps through. */
export function wildlife(scene: Scene, room: Pick<RoomSnapshot, "pond" | "props">, kind: string | undefined): ((seconds: number) => void) | undefined {
  let parts: Part[];
  let position: (time: number) => [number, number, number];
  if (kind === "fish" && room.pond) {
    const pond = room.pond;
    parts = [-0.2, 0.2].flatMap((side): Part[] => [
      { shape: "ball", diameter: 0.2, height: 0.03, at: [side, 0, side], colour: "#44879b", outlined: false },
      { shape: "box", width: 0.13, height: 0.02, depth: 0.13, at: [side, 0, side - 0.13], turn: [0, Math.PI / 4, 0], colour: "#44879b", outlined: false },
    ]);
    position = t => [pond.x + Math.cos(t * 0.55) * pond.radius * 0.48, 0.065, pond.z + Math.sin(t * 0.55) * pond.radius * 0.48];
  } else if (kind === "butterfly") {
    const flowers = room.props?.filter(p => p.kind === "flower-bed");
    const first = flowers?.[0];
    const next = flowers?.[1];
    if (!first || !next) return undefined;
    parts = [
      { shape: "box", width: 0.06, height: 0.07, depth: 0.27, at: [0, 0, 0], colour: palette.darkWood, outlined: false },
      ...[-1, 1].flatMap((side): Part[] => [
        { shape: "ball", diameter: 0.35, height: 0.08, at: [side * 0.15, 0.09, 0.06], turn: [0, 0, side * 0.5], colour: "#e2ab57" },
        { shape: "ball", diameter: 0.21, height: 0.07, at: [side * 0.12, 0.06, -0.13], colour: palette.butter },
      ]),
    ];
    position = t => {
      const along = (1 - Math.cos(t * 0.46)) / 2;
      return [first.x + (next.x - first.x) * along, 1.24 + 0.18 * Math.sin(t * 3),
        first.z + (next.z - first.z) * along + 0.45 * Math.sin(t * 1.2)];
    };
  } else {
    const recipe = kind ? wildlifeRecipe(kind) : undefined;
    const move = recipe && motionOf(recipe.motion, room);
    if (!recipe || !move) return undefined;
    parts = recipe.parts;
    position = move;
  }

  const mesh = buildParts(scene, "wildlife", parts);
  if (!mesh) return undefined;
  mesh.isPickable = false;
  mesh.unfreezeWorldMatrix();
  let elapsed = 0;
  const pose = (seconds: number) => {
    elapsed += Math.min(seconds, 0.1);
    const [x, y, z] = position(elapsed);
    const [aheadX, , aheadZ] = position(elapsed + 0.05);
    mesh.position.set(x, y, z);
    mesh.rotation.y = Math.atan2(aheadX - x, aheadZ - z);
  };
  pose(0);
  return globalThis.matchMedia?.("(prefers-reduced-motion: reduce)").matches ? undefined : pose;
}

/** Where an admin's creature is at a time, or nothing where the room has nothing for it to move by. */
function motionOf(motion: WildlifeMotion, room: Pick<RoomSnapshot, "pond" | "props">): ((time: number) => [number, number, number]) | undefined {
  const { speed, height, bob = 0, sway = 0 } = motion;
  if (motion.kind === "orbit") {
    const pond = room.pond;
    return pond ? (t => [pond.x + Math.cos(t * speed) * pond.radius * 0.48, height, pond.z + Math.sin(t * speed) * pond.radius * 0.48]) : undefined;
  }
  if (motion.kind === "shuttle") {
    const props = room.props?.filter(p => p.kind === motion.prop);
    const [first, next] = [props?.[0], props?.[1]];
    return first && next && (t => {
      const along = (1 - Math.cos(t * speed)) / 2;
      return [first.x + (next.x - first.x) * along, height + bob * Math.sin(t * 3), first.z + (next.z - first.z) * along + sway * Math.sin(t * 1.2)];
    });
  }
  const reach = (motion.reach ?? 2) / 2;
  return t => [Math.cos(t * speed) * reach, height + bob * Math.sin(t * 3), Math.sin(t * speed * 1.3) * reach + sway * Math.sin(t * 1.2)];
}

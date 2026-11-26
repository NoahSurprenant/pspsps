// The built-in meshes a prop design's "model" part names: low-poly shapes with a colour for every vertex, made from CC0 glTF
// models by scripts/mesh-model.mjs (see models/README.md). The game draws them flat, like its other parts.
import townHotel from "./models/townHotel.json";
import townHouse from "./models/townHouse.json";
import townMall from "./models/townMall.json";
import townSquareCoffee from "./models/townSquareCoffee.json";
import townSquareClub from "./models/townSquareClub.json";
import townSquareClothes from "./models/townSquareClothes.json";

export interface MeshData {
  positions: number[];
  indices: number[];
  /** One hex colour per vertex. */
  colours: string[];
}

const meshes = new Map<string, MeshData>([
  ["town-house", townHouse as MeshData],
  ["town-hotel", townHotel as MeshData],
  ["town-mall", townMall as MeshData],
  ["town-square-coffee", townSquareCoffee as MeshData],
  ["town-square-club", townSquareClub as MeshData],
  ["town-square-clothes", townSquareClothes as MeshData],
]);

export function modelData(name: string): MeshData | undefined {
  return meshes.get(name);
}

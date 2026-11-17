import { NullEngine, Scene, VertexBuffer } from "@babylonjs/core";
import { describe, expect, it } from "vitest";
import { buildParts, outline, propKinds, propParts, type Part } from "./props";
import type { PropView } from "./types";

/** Every vertex of <prop>, drawn on its own (outline included). */
function verticesOf(prop: PropView): { x: number; y: number; z: number }[] {
  const mesh = buildParts(new Scene(new NullEngine()), "prop", propParts(prop));
  const positions = mesh?.getVerticesData(VertexBuffer.PositionKind) ?? [];
  const vertices = [];
  for (let i = 0; i < positions.length; i += 3) {
    vertices.push({ x: positions[i] ?? 0, y: positions[i + 1] ?? 0, z: positions[i + 2] ?? 0 });
  }
  return vertices;
}

describe("props", () => {
  // The kinds the server places (src/Pspsps.Domain/Rooms/Prop.cs, PropKinds.All): each must have a drawing.
  it("draws every kind of prop the server places", () => {
    expect(propKinds).toEqual(expect.arrayContaining(
      ["tree", "bench", "lamp", "flower-bed", "cafe-table", "counter", "plant", "cat-tree"]));
  });

  // The server keeps each prop's circle clear of the doors, the pond, the spawn point and the walks between them
  // (PropPlacement); a drawing that spilled out of it could still stand on them.
  it.each(propKinds)("draws a %s inside its circle, whatever its size and facing", (kind) => {
    for (const [radius, facing] of [[1, 0], [0.5, Math.PI / 2], [2, 2.5]] as const) {
      const prop = { kind, x: 3, z: -2, radius, facing };
      const vertices = verticesOf(prop);

      expect(vertices.length).toBeGreaterThan(0);
      const reach = Math.max(...vertices.map((v) => Math.hypot(v.x - prop.x, v.z - prop.z)));
      expect(reach, `${kind} at radius ${radius}`).toBeLessThanOrEqual(radius + 1e-6);
      // Standing on the floor: only an outline's width below it.
      expect(Math.min(...vertices.map((v) => v.y))).toBeGreaterThanOrEqual(-outline - 1e-6);
    }
  });

  it("turns a prop to face its way: a bench facing left has its seat to the left", () => {
    const front = (facing: number) => {
      const [seat, back] = propParts({ kind: "bench", x: 0, z: 0, radius: 1, facing });
      if (!seat || !back) throw new Error("no bench");
      return { x: seat.at[0] - back.at[0], z: seat.at[2] - back.at[2] };
    };

    expect(front(0).z).toBeLessThan(0);
    expect(front(Math.PI / 2).x).toBeLessThan(0);
    expect(Math.abs(front(Math.PI / 2).z)).toBeLessThan(1e-9);
  });

  it("draws nothing for a kind it doesn't know (a newer server's)", () => {
    expect(propParts({ kind: "statue", x: 0, z: 0, radius: 1, facing: 0 })).toEqual([]);
  });

  it("merges everything into one mesh, with one material, that never takes the pointer", () => {
    const scene = new Scene(new NullEngine());
    const parts = ["tree", "bench", "lamp"].flatMap((kind, i) => propParts({ kind, x: i * 3, z: 0, radius: 1, facing: 0 }));

    const mesh = buildParts(scene, "decor", parts);

    expect(scene.meshes).toEqual([mesh]);
    expect(scene.materials).toHaveLength(1);
    expect(mesh?.isPickable).toBe(false);
    expect(mesh?.useVertexColors).toBe(true);
  });

  it("outlines each part with an inside-out copy, unless the part says not to", () => {
    const scene = new Scene(new NullEngine());
    const part: Part = { shape: "box", width: 1, height: 1, depth: 1, at: [0, 0.5, 0], colour: "#ffffff" };
    const count = (parts: Part[]) => buildParts(scene, "p", parts)?.getTotalVertices();

    expect(count([part])).toBe(2 * (count([{ ...part, outlined: false }]) ?? 0));
  });

  it("builds nothing from no parts", () => {
    expect(buildParts(new Scene(new NullEngine()), "decor", [])).toBeUndefined();
  });
});

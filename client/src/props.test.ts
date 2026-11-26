import { NullEngine, Scene, VertexBuffer } from "@babylonjs/core";
import { describe, expect, it } from "vitest";
import serverProps from "../../src/Pspsps.Domain/Rooms/Prop.cs?raw";
import { besideProp, buildParts, outline, propKinds, propParts, type Part } from "./props";
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
  it("draws a model part as its mesh, in its own colours and outlined", () => {
    const mesh = buildParts(new Scene(new NullEngine()), "house", [{ shape: "model", model: "town-square-coffee", scale: 4, at: [0, 0, 0], colour: "#ffffff" }]);
    const positions = mesh?.getVerticesData(VertexBuffer.PositionKind) ?? [];
    const colours = new Set<string>();
    const data = mesh?.getVerticesData(VertexBuffer.ColorKind) ?? [];
    for (let i = 0; i < data.length; i += 4) {
      colours.add(`${data[i]?.toFixed(2)}${data[i + 1]?.toFixed(2)}${data[i + 2]?.toFixed(2)}`);
    }

    // Twice the vertices: the mesh and its outline. Several colours, not the part's one.
    expect(positions.length / 3).toBe(2212);
    expect(colours.size).toBeGreaterThan(5);
  });

  it("builds a model part together with boxes and balls", () => {
    const parts: Part[] = [
      { shape: "box", width: 1, height: 1, depth: 1, at: [3, 0.5, 0], colour: "#c98f5a" },
      { shape: "model", model: "town-square-coffee", scale: 3, at: [0, 0, 0], colour: "#ffffff" },
      { shape: "ball", diameter: 1, at: [-3, 0.5, 0], colour: "#6fae5f" },
    ];

    expect(buildParts(new Scene(new NullEngine()), "mixed", parts)?.getTotalVertices()).toBeGreaterThan(2212);
  });

  // The kinds the server places, read from its source (src/Pspsps.Domain/Rooms/Prop.cs, PropKinds): a kind added there
  // and not drawn here would be missing from the room without a word.
  it("draws exactly the kinds of prop the server places", () => {
    const kinds = [...serverProps.matchAll(/public const string \w+ = "([^"]+)";/g)].map((m) => m[1]);

    expect(kinds.length).toBeGreaterThan(0);
    expect([...propKinds].sort()).toEqual(kinds.sort());
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

  it("merges everything into one mesh, with one material", () => {
    const scene = new Scene(new NullEngine());
    const parts = ["tree", "bench", "lamp"].flatMap((kind, i) => propParts({ kind, x: i * 3, z: 0, radius: 1, facing: 0 }));

    const mesh = buildParts(scene, "decor", parts);

    expect(scene.meshes).toEqual([mesh]);
    expect(scene.materials).toHaveLength(1);
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

describe("besideProp", () => {
  const floor = { width: 20, depth: 12 };
  const bench = { kind: "bench", x: 4, z: -2, radius: 1, facing: 0 };
  const tree = { kind: "tree", x: 4, z: -4.5, radius: 1.2, facing: 0 };

  // A tap on a prop (its seat, a tree's crown) walks the cat to the floor beside it, on the side tapped.
  it("walks to just outside the prop tapped, on the side of the tap", () => {
    const spot = besideProp([bench], floor, 4.6, -2) ?? { x: Number.NaN, z: Number.NaN };

    expect(spot.z).toBeCloseTo(-2);
    expect(spot.x).toBeGreaterThan(4 + 1);
    expect(spot.x).toBeLessThan(4 + 1.5);
  });

  it("walks round to the front from a tap on the prop's middle", () => {
    const spot = besideProp([bench], floor, 4, -2) ?? { x: Number.NaN, z: Number.NaN };

    expect(spot.x).toBeCloseTo(4);
    expect(spot.z).toBeLessThan(-2 - 1);
  });

  it("never stops on another prop or off the floor", () => {
    // Tapped on the front of the bench: straight ahead is the tree, so the cat goes another way.
    const spot = besideProp([bench, tree], floor, 4, -2.6) ?? { x: Number.NaN, z: Number.NaN };

    for (const prop of [bench, tree]) {
      expect(Math.hypot(spot.x - prop.x, spot.z - prop.z)).toBeGreaterThan(prop.radius);
    }
    expect(Math.abs(spot.x)).toBeLessThanOrEqual(10);
    expect(Math.abs(spot.z)).toBeLessThanOrEqual(6);
  });

  it("is nothing for a tap on no prop (the wall, the backdrop)", () => {
    expect(besideProp([bench], floor, 0, 6)).toBeUndefined();
  });
});

describe("growth", () => {
  const spot = (kind: string): PropView => ({ kind, x: 0, z: 0, radius: 1.4, facing: 0 }) as PropView;

  it("draws a growing prop's extra parts only at the stages its design says", () => {
    const tree = (stage: number): number => propParts(spot("pear-tree"), stage).length;
    const crate = (stage: number): number => propParts(spot("packing-crate"), stage).length;

    expect(tree(0)).toBeGreaterThan(tree(1));
    expect(crate(2)).toBeGreaterThan(crate(1));
    expect(crate(1)).toBeGreaterThan(crate(0));
  });
});

import { describe, expect, it } from "vitest";
import { modelData } from "./meshes";

describe("the built-in meshes", () => {
  it.each(["town-square-coffee", "town-square-club", "town-square-clothes"])("%s is whole: triangles over real vertices, a colour for each", (name) => {
    const mesh = modelData(name);
    if (!mesh) throw new Error(`no mesh named ${name}`);
    const vertices = mesh.positions.length / 3;
    expect(mesh.indices.length % 3).toBe(0);
    expect(mesh.colours).toHaveLength(vertices);
    expect(Math.max(...mesh.indices)).toBeLessThan(vertices);
    expect(mesh.colours.every((colour) => /^#[0-9a-f]{6}$/.test(colour))).toBe(true);
  });
});

import { NullEngine } from "@babylonjs/core/Engines/nullEngine";
import { Matrix, Vector3 } from "@babylonjs/core/Maths/math.vector";
import type { AbstractMesh } from "@babylonjs/core/Meshes/abstractMesh";
import { TransformNode } from "@babylonjs/core/Meshes/transformNode";
import { Scene } from "@babylonjs/core/scene";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { Cat, loadCatModel } from "./cat";
import catGlb from "./models/cat.glb?inline";
import { collarIds, hatIds } from "./wearables";

// How what a cat wears sits on the model, measured against the model itself (in the body's units: +y up, +z forward),
// so a hat rests on the head instead of sinking into it or floating, keeps out of the ears, and a collar hugs the body.

function modelBytes(): Uint8Array {
  return Uint8Array.from(atob(catGlb.slice(catGlb.indexOf(",") + 1)), (c) => c.charCodeAt(0));
}

// The outline drawn round the coat and what the cat wears, in the model's units (cat.ts): a gap narrower than this
// shows the coat's outline through the collar.
const outline = 0.025 / 0.75;

describe("what a cat wears, on the model", () => {
  let engine: NullEngine;
  let scene: Scene;
  let cat: Cat;
  let toBody: Matrix;

  beforeEach(async () => {
    engine = new NullEngine();
    scene = new Scene(engine);
    const model = await loadCatModel(scene, modelBytes());
    const root = new TransformNode("fit", scene);
    cat = new Cat(scene, model, root, "fit", "Grey");
    const body = scene.getTransformNodeByName("fit-body");
    if (!body) {
      throw new Error("no body");
    }
    toBody = body.computeWorldMatrix(true).clone().invert();
  });

  afterEach(() => {
    scene.dispose();
    engine.dispose();
  });

  // The vertices of <meshes>, in the body's units.
  function vertices(meshes: AbstractMesh[]): Vector3[] {
    return meshes.flatMap((mesh) => {
      const positions = mesh.getVerticesData("position") ?? [];
      const toHere = mesh.computeWorldMatrix(true).multiply(toBody);
      const points: Vector3[] = [];
      for (let i = 0; i < positions.length; i += 3) {
        points.push(Vector3.TransformCoordinates(new Vector3(positions[i], positions[i + 1], positions[i + 2]), toHere));
      }
      return points;
    });
  }

  function coat(): Vector3[] {
    return vertices(scene.meshes.filter((mesh) => mesh.name === "fit-body.coat"));
  }

  function worn(slot: "hat" | "collar", part?: string): Vector3[] {
    const node = scene.getTransformNodeByName(`fit-${slot}`);
    const meshes = node?.getChildMeshes(false) ?? [];
    return vertices(part ? meshes.filter((mesh) => mesh.name.endsWith(`-${part}`)) : meshes);
  }

  // The top of the head: the flat top of the cube, behind the ears (which stand up from its front corners).
  function headTop(): number {
    return Math.max(...coat().filter((v) => v.z < 0.2).map((v) => v.y));
  }

  it("has the head-top anchor on the top of the head, not in it", () => {
    const anchor = Vector3.TransformCoordinates(Vector3.Zero(), cat.anchors.headTop.computeWorldMatrix(true).multiply(toBody));

    expect(anchor.y).toBeCloseTo(headTop(), 2);
    expect(Math.abs(anchor.x)).toBeLessThan(0.01);
  });

  it.each(hatIds)("rests the %s on the head: it neither sinks into it nor floats", (hat) => {
    cat.dress({ fur: "Grey", hat, collar: null });

    const bottom = Math.min(...worn("hat").map((v) => v.y));
    expect(bottom).toBeGreaterThan(headTop() - 0.03);
    expect(bottom).toBeLessThan(headTop() + 0.03);
  });

  it.each(hatIds)("keeps the %s out of the ears", (hat) => {
    cat.dress({ fur: "Grey", hat, collar: null });
    const top = headTop();
    // One ear (the other is its mirror): what of the coat stands above the head.
    const ear = coat().filter((v) => v.y > top + 0.005 && v.x > 0);
    const [left, right] = [Math.min(...ear.map((v) => v.x)), Math.max(...ear.map((v) => v.x))];
    const [back, front] = [Math.min(...ear.map((v) => v.z)), Math.max(...ear.map((v) => v.z))];
    const earTip = Math.max(...ear.map((v) => v.y));

    const inEar = worn("hat").filter((v) => v.y < earTip && Math.abs(v.x) > left - outline && Math.abs(v.x) < right + outline
      && v.z > back - outline && v.z < front + outline);
    expect(inEar).toEqual([]);
  });

  it.each(collarIds)("hugs the body with the %s: round it, just clear of the coat's outline, not out like a plate", (collar) => {
    cat.dress({ fur: "Grey", hat: null, collar });
    const band = worn("collar", "band");
    const [low, high] = [Math.min(...band.map((v) => v.y)), Math.max(...band.map((v) => v.y))];
    // The body where the band goes round it, and a little above and below.
    const body = coat().filter((v) => v.y > low - 0.05 && v.y < high + 0.05);
    expect(body.length).toBeGreaterThan(0);

    // Seen from above the body is an octagon (a cube with its upright edges cut off): the band stands off each of its
    // eight sides by about the same: more than the outline, which is drawn pulled towards the camera and shows through a
    // band any nearer, and not much more.
    for (let k = 0; k < 8; k++) {
      const side = new Vector3(Math.sin((k * Math.PI) / 4), 0, Math.cos((k * Math.PI) / 4));
      const reach = (points: Vector3[]) => Math.max(...points.map((v) => Vector3.Dot(v, side)));
      const gap = reach(band) - reach(body);
      expect(gap, `side ${k}`).toBeGreaterThan(outline + 0.03);
      expect(gap, `side ${k}`).toBeLessThan(0.12);
    }
  });

  it("puts the collar round the upright part of the body, under the mouth, not on the slope below it", () => {
    cat.dress({ fur: "Grey", hat: null, collar: "red-collar" });
    const band = worn("collar", "band");
    const mouth = vertices(scene.meshes.filter((mesh) => mesh.name === "fit-mouth.dark"));
    // Where the body stops narrowing towards its feet: its widest, lowest.
    const widest = Math.max(...coat().map((v) => Math.abs(v.x)));
    const upright = Math.min(...coat().filter((v) => Math.abs(v.x) > widest - 0.001).map((v) => v.y));

    expect(Math.min(...band.map((v) => v.y))).toBeGreaterThanOrEqual(upright - 0.01);
    expect(Math.max(...band.map((v) => v.y))).toBeLessThanOrEqual(Math.min(...mouth.map((v) => v.y)) + 0.01);
  });

  it.each(collarIds)("hangs the %s's tag or bell in front of its band, at the front", (collar) => {
    cat.dress({ fur: "Grey", hat: null, collar });
    const band = worn("collar", "band");
    const charm = worn("collar", collar === "bell-collar" ? "bell" : "tag");

    const front = Math.max(...band.map((v) => v.z));
    expect(Math.max(...charm.map((v) => v.z))).toBeGreaterThan(front + 0.02);
    expect(Math.abs(Math.max(...charm.map((v) => v.x)) + Math.min(...charm.map((v) => v.x)))).toBeLessThan(0.01);
  });
});

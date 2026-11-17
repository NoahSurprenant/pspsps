import { NullEngine } from "@babylonjs/core";
import { describe, expect, it } from "vitest";
import { RoomScene } from "./scene";

describe("RoomScene", () => {
  // The page draws from the moment it loads, before any room is showing (sign-in, world picker). A frame that throws
  // stops Babylon's render loop for good, so no room would ever draw after it.
  it("draws a frame before any room is showing", () => {
    const scene = new RoomScene(new NullEngine());

    expect(() => scene.renderFrame()).not.toThrow();
  });
});

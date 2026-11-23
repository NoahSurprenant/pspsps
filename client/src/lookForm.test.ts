import { describe, expect, it } from "vitest";
import { blankLook, withField, withOpenLandscape, withoutGround, withPainter } from "./lookForm";

describe("lookForm", () => {
  it("a blank look has colours and a plain floor and wall", () =>
    expect(blankLook()).toMatchObject({ floor: { painter: "planks" }, wall: { painter: "panelled" }, backdrop: { parts: [] } }));

  it("changes a colour, and sets or clears the wildlife", () => {
    const look = withField(blankLook(), "sky", "#000000");
    const moths = withField(look, "wildlife", "moth");

    expect(look.sky).toBe("#000000");
    expect(moths.wildlife).toBe("moth");
    expect("wildlife" in withField(moths, "wildlife", "")).toBe(false);
  });

  it("changes a painter's colour and keeps the rest, and takes the part away with no painter", () => {
    const look = withPainter(blankLook(), "floor", { accent: "#112233" });

    expect(look.floor).toEqual({ painter: "planks", colour: "#b08a5a", accent: "#112233" });
    expect(withPainter(look, "floor", { painter: "" }).floor).toBeUndefined();
    expect(withPainter(look, "wall", { painter: "brick" }).wall).toEqual({ painter: "brick" });
  });

  it("a ground always has a colour, and a painter only when chosen", () => {
    const ground = withPainter(blankLook(), "ground", { colour: "#445566" });
    const painted = withPainter(ground, "ground", { painter: "street" });

    expect(ground.ground).toEqual({ colour: "#445566" });
    expect(painted.ground).toEqual({ colour: "#445566", painter: "street" });
    expect(withPainter(blankLook(), "ground", { painter: "street" }).ground).toEqual({ colour: "#8a8a8a", painter: "street" });
    expect(withoutGround(painted).ground).toBeUndefined();
  });

  it("sets and clears open landscape", () => {
    expect(withOpenLandscape(blankLook(), true).openLandscape).toBe(true);
    expect("openLandscape" in withOpenLandscape(withOpenLandscape(blankLook(), true), false)).toBe(false);
  });
});

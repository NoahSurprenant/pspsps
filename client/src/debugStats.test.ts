import { describe, expect, it } from "vitest";
import { debugLines, DebugStats } from "./debugStats";

describe("DebugStats", () => {
  it("counts the frames of the last second and the longest gap of the last few", () => {
    const stats = new DebugStats();
    for (let t = 0; t <= 1000; t += 100) stats.frame(t);
    stats.frame(1400);

    expect(stats.fps(1400)).toBe(8);
    expect(stats.worstFrame(1400)).toBe(400);
    expect(stats.worstFrame(7000)).toBe(0);
  });

  it("keeps the last ping and the worst, and notes a slow one as a blip", () => {
    const stats = new DebugStats();
    stats.pinged(40, 1000);
    stats.pinged(900, 2000);
    stats.pinged(50, 3000);

    expect(stats.lastPing()).toBe(50);
    expect(stats.worstPing()).toBe(900);
    expect(stats.events.map((e) => e.text)).toEqual(["slow reply, 900 ms"]);
  });

  it("keeps only the latest few events", () => {
    const stats = new DebugStats();
    for (let i = 0; i < 10; i++) stats.note(`n${i}`, i);

    expect(stats.events.map((e) => e.text)).toEqual(["n4", "n5", "n6", "n7", "n8", "n9"]);
  });
});

describe("debugLines", () => {
  it("says the room first, then the frames, the server and the blips", () => {
    const stats = new DebugStats();
    stats.pinged(32, 4000);
    stats.note("connection dropped, reconnecting", 2000);

    const lines = debugLines(stats, { roomName: "Town Square", roomId: "town-square", spot: { x: 1.26, z: -3 }, cats: 4, connection: "Connected" }, 5000);

    expect(lines).toEqual([
      "Room: Town Square (town-square)",
      "Cat at: 1.3, -3.0  ·  4 cats here",
      "Frames: 0 fps  ·  worst gap 0 ms",
      "Server: Connected  ·  ping 32 ms (worst 32)",
      "  3s ago: connection dropped, reconnecting",
    ]);
  });

  it("says so with no room or no ping yet", () => {
    expect(debugLines(new DebugStats(), undefined, 0)).toEqual(["Room: none", "Frames: 0 fps  ·  worst gap 0 ms", "Server: no connection  ·  ping -"]);
  });
});

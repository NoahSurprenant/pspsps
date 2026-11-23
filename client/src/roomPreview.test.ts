import { beforeEach, describe, expect, it, vi } from "vitest";

const calls: string[] = [];

vi.mock("@babylonjs/core/Engines/engine", () => ({ Engine: class {
  resize(): void {}
} }));
vi.mock("./props", () => ({ loadPropDesigns: async () => { calls.push("props"); } }));
vi.mock("./scene", () => ({ RoomScene: class {
  show(): void { calls.push("show"); }
} }));
vi.mock("./looks", () => ({
  loadLooks: async () => { calls.push("loadLooks"); },
  setDraftLook: (id: string) => { calls.push(`draftLook ${id}`); },
}));

import { previewUrl, roomPreview } from "./roomPreview";

describe("previewUrl", () => {
  it("asks for the room, and for an overlay when one is chosen", () => {
    expect(previewUrl("garden")).toBe("/api/admin/rooms/garden/preview");
    expect(previewUrl("town-square", "winter party")).toBe("/api/admin/rooms/town-square/preview?overlay=winter%20party");
  });
});

describe("roomPreview", () => {
  beforeEach(() => {
    calls.length = 0;
    vi.stubGlobal("window", { addEventListener: () => {} });
    vi.stubGlobal("fetch", async () => new Response(JSON.stringify({ roomId: "shop", draftLook: { sky: "#123456" } })));
  });

  it("sets the draft's look after the scene has read the live looks, so making the scene cannot wipe it", async () => {
    await roomPreview({} as HTMLCanvasElement).show("shop");

    expect(calls.lastIndexOf("loadLooks")).toBeLessThan(calls.indexOf("draftLook shop"));
    expect(calls.indexOf("draftLook shop")).toBeLessThan(calls.indexOf("show"));
  });
});

describe("roomPreview.draw", () => {
  beforeEach(() => {
    calls.length = 0;
    vi.stubGlobal("window", { addEventListener: () => {} });
  });

  it("draws a snapshot it is given, with its look, without asking the server", async () => {
    const fetched = vi.fn();
    vi.stubGlobal("fetch", fetched);

    await roomPreview({} as HTMLCanvasElement).draw("shop", { roomId: "shop" } as never, { sky: "#123456" } as never);

    expect(calls).toContain("draftLook shop");
    expect(calls).toContain("show");
    expect(fetched).not.toHaveBeenCalled();
  });
});

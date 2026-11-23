import { describe, expect, it } from "vitest";
import { asPreviewMessage, unsavedPreviewUrl } from "./previewMessage";

describe("asPreviewMessage", () => {
  it("accepts a room to draw, with or without a look", () => {
    const message = { type: "room-preview", roomId: "shop", snapshot: { roomId: "shop" }, draftLook: { sky: "#123456" } };

    expect(asPreviewMessage(message)).toBe(message);
    expect(asPreviewMessage({ type: "room-preview", roomId: "shop", snapshot: {} })).not.toBeNull();
  });

  it.each([null, undefined, "room-preview", 3, {}, { type: "other", roomId: "shop", snapshot: {} },
    { type: "room-preview", snapshot: {} }, { type: "room-preview", roomId: "shop" }, { type: "room-preview", roomId: "shop", snapshot: null }])(
    "ignores %j",
    (data) => expect(asPreviewMessage(data)).toBeNull());
});

describe("unsavedPreviewUrl", () => {
  it("names the room, escaped", () => expect(unsavedPreviewUrl("a b")).toBe("/api/admin/rooms/a%20b/preview"));
});

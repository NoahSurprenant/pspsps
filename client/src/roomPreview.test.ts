import { describe, expect, it } from "vitest";
import { previewUrl } from "./roomPreview";

describe("previewUrl", () => {
  it("asks for the room, and for an overlay when one is chosen", () => {
    expect(previewUrl("garden")).toBe("/api/admin/rooms/garden/preview");
    expect(previewUrl("town-square", "winter party")).toBe("/api/admin/rooms/town-square/preview?overlay=winter%20party");
  });
});

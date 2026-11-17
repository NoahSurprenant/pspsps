import { describe, expect, it } from "vitest";
import { pageTitle } from "./title";

describe("pageTitle", () => {
  it("names the room first, then the game; outside a room, just the game", () => {
    expect(pageTitle("Cat Café")).toBe("Cat Café · pspsps");
    expect(pageTitle("Kitty 1234's home")).toBe("Kitty 1234's home · pspsps");
    expect(pageTitle()).toBe("pspsps");
    expect(pageTitle("")).toBe("pspsps");
  });
});

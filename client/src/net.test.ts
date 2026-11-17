import { afterEach, describe, expect, it, vi } from "vitest";
import { fetchProviders, messageOf } from "./net";

describe("messageOf", () => {
  it("keeps only the server's message from a hub error", () => {
    const error = new Error("An unexpected error occurred invoking 'Say' on the server. HubException: Say something.");

    expect(messageOf(error)).toBe("Say something.");
  });

  it("passes other errors through", () => {
    expect(messageOf(new Error("Network down"))).toBe("Network down");
    expect(messageOf("odd")).toBe("odd");
  });
});

describe("fetchProviders", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("offers no providers when the request fails, so the world list still shows", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("Failed to fetch")));

    await expect(fetchProviders()).resolves.toEqual([]);
  });

  it("returns the server's providers", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify(["Discord", "Google"]))));

    await expect(fetchProviders()).resolves.toEqual(["Discord", "Google"]);
  });
});

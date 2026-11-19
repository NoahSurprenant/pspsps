import { afterEach, describe, expect, it, vi } from "vitest";
import { NetworkBannedError, fetchNetworkBan, fetchProviders, messageOf, playAsGuest } from "./net";

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

describe("a banned network", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  const ban = { reason: "Spam", until: "2026-11-20T12:00:00Z" };

  it("gets no guest: the refusal carries the ban, to show as a banned cat's", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(
      JSON.stringify({ message: "Your cat is banned until 20 Nov 2026 12:00 UTC. Why: Spam", ban }), { status: 403 })));

    const refused = await playAsGuest().catch((error: unknown) => error);

    expect(refused).toBeInstanceOf(NetworkBannedError);
    expect((refused as NetworkBannedError).ban).toEqual(ban);
  });

  it("is asked about after a refused sign-in: its ban, or none", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify(ban), { status: 200 })));
    await expect(fetchNetworkBan()).resolves.toEqual(ban);

    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(null, { status: 204 })));
    await expect(fetchNetworkBan()).resolves.toBeNull();
  });
});

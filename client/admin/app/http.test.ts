import { afterEach, describe, expect, it, vi } from "vitest";
import { messageOf, request } from "./http";

const reply = (status: number, body?: unknown) => new Response(body === undefined ? null : JSON.stringify(body), { status });

describe("asking the server", () => {
  afterEach(() => vi.unstubAllGlobals());

  it("sends a body as JSON, and reads the JSON back; a 204 gives nothing", async () => {
    const fetcher = vi.fn().mockResolvedValueOnce(reply(200, { problems: [] })).mockResolvedValueOnce(reply(204));
    vi.stubGlobal("fetch", fetcher);

    expect(await request("PUT", "/api/admin/rooms/cafe/draft", { name: "Cafe" })).toEqual({ problems: [] });
    expect(await request("DELETE", "/api/admin/rooms/cafe/draft")).toBeNull();

    expect(fetcher.mock.calls[0]?.[1]).toMatchObject({ method: "PUT", headers: { "Content-Type": "application/json" }, body: '{"name":"Cafe"}' });
    expect(fetcher.mock.calls[1]?.[1]).toMatchObject({ method: "DELETE", headers: {}, body: undefined });
  });

  it("throws what the server said was wrong, or a plain line when it said nothing", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValueOnce(reply(400, { message: "Door x leads nowhere." })).mockResolvedValueOnce(reply(502)));

    await expect(request("POST", "/x")).rejects.toThrow("Door x leads nowhere.");
    await expect(request("POST", "/x")).rejects.toThrow("That didn't work (502).");
  });

  it("puts any error into words", () => {
    expect(messageOf(new Error("no"))).toBe("no");
    expect(messageOf("plain")).toBe("plain");
  });
});

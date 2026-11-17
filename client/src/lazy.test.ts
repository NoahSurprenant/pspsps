import { describe, expect, it, vi } from "vitest";
import { lazily } from "./lazy";

describe("lazily", () => {
  it("makes the thing once, however many ask", async () => {
    const make = vi.fn(() => Promise.resolve("scene"));
    const get = lazily(make);

    expect(await Promise.all([get(), get()])).toEqual(["scene", "scene"]);
    expect(await get()).toBe("scene");
    expect(make).toHaveBeenCalledTimes(1);
  });

  it("tries again after a failure, rather than failing for good", async () => {
    const make = vi.fn()
      .mockReturnValueOnce(Promise.reject(new Error("chunk failed to load")))
      .mockReturnValueOnce(Promise.resolve("scene"));
    const get = lazily(make);

    await expect(get()).rejects.toThrow("chunk failed to load");
    expect(await get()).toBe("scene");
    expect(make).toHaveBeenCalledTimes(2);
  });
});

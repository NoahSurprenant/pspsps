import { describe, expect, it } from "vitest";
import { linkMessage, linkUrl } from "./link";

describe("linkMessage", () => {
  it("says the cat is saved", () => {
    expect(linkMessage("?link=linked")).toEqual({ text: expect.stringContaining("Your cat is saved") as string, error: false });
  });

  it("says when the account already has a cat, and that nothing was merged", () => {
    expect(linkMessage("?link=taken")).toEqual({
      text: expect.stringContaining("already has its own cat") as string,
      error: true,
    });
  });

  it("says when it failed", () => {
    expect(linkMessage("?link=failed")?.error).toBe(true);
  });

  it("says nothing otherwise", () => {
    expect(linkMessage("")).toBeUndefined();
    expect(linkMessage("?signin=failed")).toBeUndefined();
    expect(linkMessage("?link=something-else")).toBeUndefined();
  });
});

describe("linkUrl", () => {
  it("goes to the server's link endpoint for the provider", () => {
    expect(linkUrl("Google")).toBe("/auth/link/Google");
    expect(linkUrl("a/b")).toBe("/auth/link/a%2Fb");
  });
});

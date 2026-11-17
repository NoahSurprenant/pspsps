import { describe, expect, it } from "vitest";
import { addressNote, linkUrl } from "./link";

describe("addressNote", () => {
  it("says the cat is saved", () => {
    expect(addressNote("?link=linked")).toEqual({ text: expect.stringContaining("Your cat is saved") as string, error: false });
  });

  it("says when the account already has a cat, and that nothing was merged", () => {
    expect(addressNote("?link=taken")).toEqual({
      text: expect.stringContaining("already has its own cat") as string,
      error: true,
    });
  });

  it("says why a refused link can't work, without asking to try again", () => {
    const note = addressNote("?link=refused");

    expect(note?.error).toBe(true);
    expect(note?.text).toContain("already has a login from that provider");
    expect(note?.text).not.toContain("Try again");
  });

  it("says when it failed", () => {
    expect(addressNote("?link=failed")).toEqual({ text: "Saving your cat didn't work. Try again.", error: true });
  });

  it("says when signing in failed, signed in or not", () => {
    expect(addressNote("?signin=failed")).toEqual({ text: "Signing in didn't work. Try again.", error: true });
  });

  it("says nothing otherwise", () => {
    expect(addressNote("")).toBeUndefined();
    expect(addressNote("?link=something-else")).toBeUndefined();
    expect(addressNote("?signin=other")).toBeUndefined();
  });
});

describe("linkUrl", () => {
  it("goes to the server's link endpoint for the provider", () => {
    expect(linkUrl("Google")).toBe("/auth/link/Google");
    expect(linkUrl("a/b")).toBe("/auth/link/a%2Fb");
  });
});

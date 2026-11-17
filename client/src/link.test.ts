import { describe, expect, it } from "vitest";
import { addressNote, linkUrl } from "./link";

describe("addressNote", () => {
  it("says the cat is saved", () => {
    expect(addressNote("?link=linked", true)).toEqual({ text: expect.stringContaining("Your cat is saved") as string, error: false });
  });

  it("says when the account already has a cat, and that nothing was merged", () => {
    expect(addressNote("?link=taken", true)).toEqual({
      text: expect.stringContaining("already has its own cat") as string,
      error: true,
    });
  });

  it("says why a refused link can't work, without asking to try again", () => {
    const note = addressNote("?link=refused", true);

    expect(note?.error).toBe(true);
    expect(note?.text).toContain("already has a login from that provider");
    expect(note?.text).not.toContain("Try again");
  });

  it("says when it failed", () => {
    expect(addressNote("?link=failed", true)).toEqual({ text: "Saving your cat didn't work. Try again.", error: true });
  });

  it("says a signed-out player's guest cat couldn't be found, not to try again", () => {
    expect(addressNote("?link=failed", false)).toEqual({
      text: "Your guest cat couldn't be found. Sign in or play as a new guest.",
      error: true,
    });
  });

  it("says when signing in failed, signed in or not", () => {
    for (const signedIn of [true, false]) {
      expect(addressNote("?signin=failed", signedIn)).toEqual({ text: "Signing in didn't work. Try again.", error: true });
    }
  });

  it("says nothing otherwise", () => {
    expect(addressNote("", true)).toBeUndefined();
    expect(addressNote("?link=something-else", true)).toBeUndefined();
    expect(addressNote("?signin=other", false)).toBeUndefined();
  });
});

describe("linkUrl", () => {
  it("goes to the server's link endpoint for the provider", () => {
    expect(linkUrl("Google")).toBe("/auth/link/Google");
    expect(linkUrl("a/b")).toBe("/auth/link/a%2Fb");
  });
});

import { describe, expect, it } from "vitest";
import { addressNote, LINK_RESULTS, linkUrl, loginUrl } from "./link";

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

  it("says a sign-in refused for a banned network was, once its ban is over (while it holds, the page shows the ban)", () => {
    expect(addressNote("?signin=banned", false)).toEqual({
      text: "Signing in was refused from this network for a while. Try again.",
      error: true,
    });
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

describe("loginUrl", () => {
  it("goes to the server's sign-in endpoint for the provider", () => {
    expect(loginUrl("Discord")).toBe("/auth/login/Discord");
    expect(loginUrl("a/b")).toBe("/auth/login/a%2Fb");
  });
});

describe("LINK_RESULTS", () => {
  // The same four as the server's AuthRedirects (src/Pspsps.Server/Auth/AuthRedirects.cs).
  it("are what the server sends, and each has a note", () => {
    expect([...LINK_RESULTS].sort()).toEqual(["failed", "linked", "refused", "taken"]);
    for (const result of LINK_RESULTS) {
      expect(addressNote(`?link=${result}`, true)).toBeDefined();
    }
  });
});

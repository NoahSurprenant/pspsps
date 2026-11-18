import { describe, expect, it } from "vitest";
import {
  Notices, bannedLines, canLift, doneMessage, inForce, mutedNote, mutedToast, requestFor, sayWhy, standingLines, untilText,
} from "./moderation";

const utc = { locale: "en-GB", timeZone: "UTC" };

describe("untilText", () => {
  it("says the end in the player's time, or for good", () => {
    expect(untilText("2026-11-18T15:04:00+00:00", utc)).toBe("until 18 Nov 2026, 15:04");
    expect(untilText("2026-11-18T15:04:00+00:00", { locale: "en-GB", timeZone: "Europe/Paris" })).toBe("until 18 Nov 2026, 16:04");
    expect(untilText(null, utc)).toBe("for good");
  });
});

describe("a banned or muted player", () => {
  it("is told until when and why", () => {
    expect(bannedLines({ reason: "Spamming", until: "2026-11-19T12:00:00Z" }, utc))
      .toEqual(["Your cat is banned until 19 Nov 2026, 12:00.", "Why: Spamming"]);
    expect(bannedLines({ reason: "Threats", until: null }, utc)[0]).toBe("Your cat is banned for good.");
    expect(mutedNote({ reason: "Rude", until: "2026-11-18T13:00:00Z" }, utc)).toBe("You're muted until 18 Nov 2026, 13:00. Why: Rude");
    expect(mutedToast({ reason: "Rude", until: "2026-11-18T13:00:00Z" }, utc)).toBe("An admin muted you until 18 Nov 2026, 13:00. Why: Rude");
  });

  it("is free again at the end", () => {
    const mute = { reason: "Rude", until: "2026-11-18T13:00:00Z" };
    expect(inForce(mute, new Date("2026-11-18T12:59:59Z"))).toBe(true);
    expect(inForce(mute, new Date("2026-11-18T13:00:00Z"))).toBe(false);
    expect(inForce({ reason: "Threats", until: null }, new Date("2099-01-01T00:00:00Z"))).toBe(true);
    expect(inForce(null)).toBe(false);
  });
});

describe("an admin's view of a card", () => {
  it("shows the cat's ban and mute, or that it has none", () => {
    expect(standingLines({ isAdmin: false, ban: null, mute: null })).toEqual(["Not banned or muted."]);
    expect(standingLines({
      isAdmin: false,
      ban: { reason: "Spam", until: null },
      mute: { reason: "Rude", until: "2026-11-18T13:00:00Z" },
    }, utc)).toEqual(["Banned for good: Spam", "Muted until 18 Nov 2026, 13:00: Rude"]);
  });

  it("offers Lift only when there is something to lift", () => {
    expect(canLift({ isAdmin: false, ban: null, mute: null })).toBe(false);
    expect(canLift({ isAdmin: false, ban: null, mute: { reason: "Rude", until: "2026-11-18T13:00:00Z" } })).toBe(true);
  });

  it("says why for everything but a lift", () => {
    expect(requestFor("warn", "   ")).toBe(sayWhy);
    expect(requestFor("ban", "", "permanent")).toBe(sayWhy);
    expect(requestFor("lift", "")).toEqual({ action: "lift", reason: "" });
    expect(requestFor("mute", "  Rude in chat ", "1h")).toEqual({ action: "mute", reason: "Rude in chat", length: "1h" });
    expect(requestFor("warn", "Be nice", "1h")).toEqual({ action: "warn", reason: "Be nice" });
  });

  it("says what was done", () => {
    expect(doneMessage({ action: "mute", reason: "Rude", length: "24h" }, "Felix")).toBe("Felix is muted for 24 hours.");
    expect(doneMessage({ action: "ban", reason: "Spam", length: "permanent" }, "Felix")).toBe("Felix is banned for good.");
    expect(doneMessage({ action: "ban", reason: "Spam", length: "7d" }, "Felix")).toBe("Felix is banned for 7 days.");
    expect(doneMessage({ action: "warn", reason: "Be nice" }, "Felix")).toBe("Felix was warned.");
    expect(doneMessage({ action: "lift", reason: "" }, "Felix")).toBe("Felix can play and chat again.");
  });
});

describe("a warning", () => {
  it("stays to show again until the player closes it, through any change of view", () => {
    const notices = new Notices();
    expect(notices.current).toBeUndefined();

    notices.show("Please keep it friendly.");
    // A room loaded meanwhile closed the dialog: still to show.
    expect(notices.current).toBe("Please keep it friendly.");

    notices.closed();
    expect(notices.current).toBeUndefined();
  });

  it("shows one at a time, in order", () => {
    const notices = new Notices();
    notices.show("First");
    notices.show("Second");

    expect(notices.current).toBe("First");
    notices.closed();
    expect(notices.current).toBe("Second");
  });
});

import { describe, expect, it } from "vitest";
import {
  maxNoteLength, muteMessage, Mutes, newReportDraft, reportReasons, reportRequest, reportSentMessage, safetyButtons,
} from "./safety";
import type { MutedView } from "./types";

const felix: MutedView = { playerId: "felix", displayName: "Felix", furColor: "Black" };
const garfield: MutedView = { playerId: "garfield", displayName: "Garfield", furColor: "Orange" };

describe("Mutes", () => {
  it("knows whom the player muted, and says who is newly muted", () => {
    const mutes = new Mutes();

    expect(mutes.set([felix])).toEqual(["felix"]);
    expect(mutes.has("felix")).toBe(true);
    expect(mutes.has("garfield")).toBe(false);

    expect(mutes.set([felix, garfield])).toEqual(["garfield"]);
    expect(mutes.list).toEqual([felix, garfield]);
  });

  it("forgets whom the player unmuted", () => {
    const mutes = new Mutes();
    mutes.set([felix, garfield]);

    expect(mutes.set([garfield])).toEqual([]);

    expect(mutes.has("felix")).toBe(false);
    expect(mutes.list).toEqual([garfield]);
  });
});

describe("safetyButtons", () => {
  it("offers Mute and Report, or Unmute for a cat muted already", () => {
    expect(safetyButtons(false)).toEqual([{ action: "mute", label: "Mute" }, { action: "report", label: "Report" }]);
    expect(safetyButtons(true).map((b) => b.action)).toEqual(["unmute", "report"]);
  });
});

describe("the report form", () => {
  it("lists the reasons the server takes", () => {
    expect(reportReasons.map((r) => r.label)).toEqual(["Mean or hurtful chat", "Inappropriate name", "Spam", "Something else"]);
    expect(reportReasons.map((r) => r.reason)).toEqual(["MeanChat", "InappropriateName", "Spam", "SomethingElse"]);
  });

  it("starts with no reason and no note, muting the cat too", () => {
    expect(newReportDraft()).toEqual({ reason: undefined, note: "", alsoMute: true });
  });

  it("needs a reason", () => {
    expect(reportRequest(newReportDraft())).toEqual({ problem: "Pick a reason." });
  });

  it("sends the reason, the note trimmed (blank is none), and whether to mute", () => {
    expect(reportRequest({ reason: "Spam", note: "  over and over  ", alsoMute: false }))
      .toEqual({ reason: "Spam", note: "over and over", alsoMute: false });
    expect(reportRequest({ reason: "MeanChat", note: "   ", alsoMute: true }))
      .toEqual({ reason: "MeanChat", note: null, alsoMute: true });
  });

  it("refuses a note over the limit", () => {
    expect(reportRequest({ reason: "SomethingElse", note: "a".repeat(maxNoteLength), alsoMute: true })).not.toHaveProperty("problem");
    expect(reportRequest({ reason: "SomethingElse", note: "a".repeat(maxNoteLength + 1), alsoMute: true }))
      .toEqual({ problem: `A note is at most ${maxNoteLength} characters.` });
  });

  it("says the report went, and whether the cat is muted now", () => {
    expect(reportSentMessage("Felix", true)).toBe("Thanks. Your report was sent, and Felix is muted.");
    expect(reportSentMessage("Felix", false)).toBe("Thanks. Your report was sent.");
    expect(muteMessage("Felix", true)).toContain("Felix is muted.");
    expect(muteMessage("Felix", false)).toBe("Felix isn't muted any more.");
  });
});

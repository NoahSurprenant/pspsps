import { describe, expect, it } from "vitest";
import {
  decidedMessage, decisionChoices, decisionRequest, liftLabel, logLine, reportFacts, reportHeading, sanctionLine,
  type OpenReport, type SanctionedPlayer,
} from "./admin";
import { sayWhy } from "./moderation";

const utc = { locale: "en-GB", timeZone: "UTC" };

const ann = { byId: "a", byName: "Ann", since: "2026-11-18T14:00:00Z" };

describe("the admin page", () => {
  it("says each ban and mute: until when, by whom, since when, and why", () => {
    expect(sanctionLine("Banned", { ...ann, reason: "Spam", until: null }, utc))
      .toBe("Banned for good by Ann on 18 Nov 2026, 14:00: Spam");
    expect(sanctionLine("Muted", { ...ann, reason: "Rude", until: "2026-11-18T15:00:00Z" }, utc))
      .toBe("Muted until 18 Nov 2026, 15:00 by Ann on 18 Nov 2026, 14:00: Rude");
  });

  it("names what Lift lifts", () => {
    const felix: SanctionedPlayer = { playerId: "f", displayName: "Felix", ban: null, mute: null };
    const ban = { ...ann, reason: "Spam", until: null };
    expect(liftLabel({ ...felix, ban })).toBe("Lift Felix's ban");
    expect(liftLabel({ ...felix, mute: ban })).toBe("Lift Felix's mute");
    expect(liftLabel({ ...felix, ban, mute: ban })).toBe("Lift Felix's ban and mute");
  });

  it("tells the audit trail as sentences", () => {
    const line = { playerId: "f", playerName: "Felix", byId: "a", byName: "Ann", at: "2026-11-18T14:00:00Z", until: null };
    expect(logLine({ ...line, kind: "Banned", reason: "Spam" }, utc)).toBe("18 Nov 2026, 14:00 · Ann banned Felix for good: Spam");
    expect(logLine({ ...line, kind: "Muted", reason: "Rude", until: "2026-11-18T15:00:00Z" }, utc))
      .toBe("18 Nov 2026, 14:00 · Ann muted Felix until 18 Nov 2026, 15:00: Rude");
    expect(logLine({ ...line, kind: "Warned", reason: "Be nice" }, utc)).toBe("18 Nov 2026, 14:00 · Ann warned Felix: Be nice");
    expect(logLine({ ...line, kind: "BanLifted", reason: null }, utc)).toBe("18 Nov 2026, 14:00 · Ann lifted Felix's ban");
  });
});

describe("a report on the admin page", () => {
  const report: OpenReport = {
    id: "r1",
    reporterId: "t",
    reporterName: "Tom",
    reportedId: "f",
    reportedName: "Felix",
    reason: "MeanChat",
    note: null,
    place: "Town Square, Tabby",
    filedAt: "2026-11-18T14:00:00Z",
    context: [],
  };

  it("says who reported whom, why, where and when", () => {
    expect(reportHeading(report)).toBe("Tom reported Felix");
    expect(reportFacts(report, utc)).toBe("Mean or hurtful chat · Town Square, Tabby · 18 Nov 2026, 14:00");
  });

  it("offers each decision, a reason needed for all but Dismiss", () => {
    expect(decisionChoices.map((choice) => choice.label)).toEqual([
      "Warn", "Mute for 1 hour", "Mute for 24 hours", "Mute for 7 days", "Ban for 24 hours", "Ban for 7 days", "Ban for good",
    ]);
    expect(decisionRequest("mute:24h", " Rude ")).toEqual({ action: "mute", reason: "Rude", length: "24h" });
    expect(decisionRequest("ban:permanent", "Threats")).toEqual({ action: "ban", reason: "Threats", length: "permanent" });
    expect(decisionRequest("warn", "Be nice")).toEqual({ action: "warn", reason: "Be nice" });
    expect(decisionRequest("warn", "  ")).toBe(sayWhy);
    expect(decisionRequest("dismiss", "")).toEqual({ action: "dismiss" });
  });

  it("says what was decided", () => {
    expect(decidedMessage("dismiss", report)).toBe("Tom's report of Felix is dismissed.");
    expect(decidedMessage("ban:7d", report)).toBe("Felix is banned for 7 days; the report is closed.");
  });
});

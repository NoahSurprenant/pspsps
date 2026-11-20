import { describe, expect, it } from "vitest";
import { calendar, fieldsOf, onAt, summary, windowOf, windowProblem } from "./overlayWindow";

const winter = { start: "2026-12-01", end: "2027-01-06", yearly: true };

describe("the overlay window picker", () => {
  it("turns dates into the server's window and back", () => {
    const window = windowOf(winter);

    expect(window).toEqual({ starts: "2026-12-01T00:00:00Z", ends: "2027-01-06T00:00:00Z", yearly: true });
    expect(fieldsOf(window)).toEqual(winter);
    expect(fieldsOf({ starts: "2026-03-01T00:00:00+00:00", ends: "2026-06-01T00:00:00+00:00" }).yearly).toBe(false);
  });

  it("refuses what the server would refuse", () => {
    expect(windowProblem(winter)).toBeNull();
    expect(windowProblem({ start: "", end: "2026-12-01", yearly: false })).toBe("Pick both dates.");
    expect(windowProblem({ start: "2026-12-08", end: "2026-12-01", yearly: false })).toBe("It ends before it starts.");
    expect(windowProblem({ start: "2026-03-01", end: "2027-03-01", yearly: true })).toBe("It starts and ends on the same day of the year.");
  });

  it("decides a day as the server does: a yearly window repeats, runs over New Year and is off on its end day", () => {
    expect([onAt(winter, "2026-12-01"), onAt(winter, "2030-01-02"), onAt(winter, "2030-01-06"), onAt(winter, "2026-07-01")]).toEqual([true, true, false, false]);
    expect([onAt({ ...winter, yearly: false }, "2026-12-20"), onAt({ ...winter, yearly: false }, "2028-12-20")]).toEqual([true, false]);
  });

  it("says the window in words, and draws the year", () => {
    expect(summary(winter)).toBe("1 Dec to 6 Jan, every year");
    expect(summary({ start: "2026-12-01", end: "2026-12-08", yearly: false })).toBe("1 Dec 2026 to 8 Dec 2026");
    const year = calendar(winter, 2026);
    expect(year).toHaveLength(12);
    expect([year[0]?.[0], year[0]?.[5], year[11]?.[0], year[5]?.[0]]).toEqual([true, false, true, false]);
    expect(year[1]).toHaveLength(28);
  });
});

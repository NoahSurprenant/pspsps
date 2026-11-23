import { describe, expect, it } from "vitest";
import { blankOverlay, propsFrom, withLook, withWindow } from "./overlayData";
import { fieldsOf, windowProblem } from "./overlayWindow";

describe("overlayData", () => {
  it("a blank overlay has a valid yearly window and changes nothing", () => {
    const overlay = blankOverlay("winter");

    expect(windowProblem(fieldsOf(overlay.window))).toBeNull();
    expect(overlay).toMatchObject({ id: "winter", adds: [], removes: [], window: { yearly: true } });
  });

  it("sets the window from the picker and the look from the select", () => {
    const moved = withWindow(blankOverlay("a"), { start: "2026-06-01", end: "2026-07-01", yearly: false });

    expect(moved.window).toEqual({ starts: "2026-06-01T00:00:00Z", ends: "2026-07-01T00:00:00Z", yearly: false });
    expect(withLook(moved, "winter").look).toBe("winter");
    expect(withLook(withLook(moved, "winter"), "").look).toBeNull();
  });

  it("reads a list of props, and says what is wrong otherwise", () => {
    expect(propsFrom("[]", "Adds")).toEqual({ props: [] });
    expect(propsFrom("{}", "Adds")).toEqual({ problem: "Adds is a JSON list of props." });
    expect(propsFrom("[", "Removes")).toEqual({ problem: "Removes isn't valid JSON." });
  });
});

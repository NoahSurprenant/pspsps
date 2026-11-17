import AxeBuilder from "@axe-core/playwright";
import { expect, test } from "@playwright/test";
import { contextFor, SCREENS, type Size } from "./screens";

// Accessibility, checked by axe-core against WCAG 2.x A and AA (2.2 included): each screen at a phone and a desktop
// size, with production's sign-in buttons. A serious or critical violation fails the test. The 3D scene is a canvas,
// which axe can't see into; the HTML over it (sign-in, the world list, the room's bars) is what is checked.

const SIZES: Size[] = [
  { device: "phone", width: 390, height: 844, phone: true },
  { device: "desktop", width: 1280, height: 720, phone: false },
];

const TAGS = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"];

for (const screen of SCREENS) {
  for (const size of SIZES) {
    test(`${screen.name} is accessible on a ${size.device}`, async ({ browser }) => {
      const context = await contextFor(browser, size);
      try {
        const page = await context.newPage();
        await screen.open(page);
        const { violations } = await new AxeBuilder({ page }).withTags(TAGS).analyze();
        const serious = violations
          .filter((v) => v.impact === "serious" || v.impact === "critical")
          .flatMap((v) => v.nodes.map((node) => `${v.id} (${v.impact}): ${node.target.join(" ")} - ${v.help}`));
        expect(serious, `${screen.name} on a ${size.device}`).toEqual([]);
      } finally {
        await context.close();
      }
    });
  }
}

// The room is played with the pointer and the buttons: the canvas is never a Tab stop, not even once the pointer has
// been over it (Babylon sets the canvas's tabindex again as the pointer moves).
test("the room's canvas is not a Tab stop, after the pointer has been over it", async ({ browser }) => {
  const context = await contextFor(browser, SIZES[1]!);
  try {
    const page = await context.newPage();
    await SCREENS.find((screen) => screen.name === "town-square")!.open(page);
    const canvas = await page.locator("#game").boundingBox();
    if (!canvas) throw new Error("no canvas");
    await page.mouse.move(canvas.x + canvas.width / 2, canvas.y + canvas.height / 2);
    await page.mouse.move(canvas.x + canvas.width / 3, canvas.y + canvas.height / 3);

    await expect(page.locator("#game")).toHaveAttribute("tabindex", "-1");
  } finally {
    await context.close();
  }
});

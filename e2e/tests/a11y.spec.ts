import AxeBuilder from "@axe-core/playwright";
import { expect, test, type Page } from "@playwright/test";

// Accessibility, checked by axe-core against WCAG 2.x A and AA (2.2 included): each screen at a phone and a desktop
// size. A serious or critical violation fails the test. The 3D scene is a canvas, which axe can't see into; the HTML
// over it (sign-in, the world list, the room's bars) is what is checked.

const SIZES = [
  { device: "phone", width: 390, height: 844, phone: true },
  { device: "desktop", width: 1280, height: 720, phone: false },
] as const;

const TAGS = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"];

const SCREENS: { name: string; open(page: Page): Promise<void> }[] = [
  {
    name: "sign-in",
    open: async (page) => {
      await page.goto("/");
      await expect(page.getByRole("button", { name: "Play as a guest" })).toBeVisible();
    },
  },
  {
    name: "worlds",
    open: async (page) => {
      await page.goto("/");
      await page.getByRole("button", { name: "Play as a guest" }).click();
      await expect(page.getByRole("button", { name: /^Tabby · / })).toBeVisible();
    },
  },
  {
    name: "town-square",
    open: async (page) => {
      await page.goto("/");
      await page.getByRole("button", { name: "Play as a guest" }).click();
      await page.getByRole("button", { name: /^Siamese · / }).click();
      await expect(page.locator(".room-bar strong")).toHaveText("Town Square");
    },
  },
];

for (const screen of SCREENS) {
  for (const size of SIZES) {
    test(`${screen.name} is accessible on a ${size.device}`, async ({ browser }) => {
      const context = await browser.newContext({
        viewport: { width: size.width, height: size.height },
        ...(size.phone ? { isMobile: true, hasTouch: true, deviceScaleFactor: 2 } : {}),
      });
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

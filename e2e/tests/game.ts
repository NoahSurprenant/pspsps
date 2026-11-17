import { expect, test as base, type Browser, type Page } from "@playwright/test";

/** A page that fails its test on any console error or uncaught exception (the "No camera defined" kind). */
export const test = base.extend<{ page: Page }>({
  page: async ({ page }, use) => {
    const errors = watchErrors(page);
    await use(page);
    expect(errors, "console errors and uncaught exceptions").toEqual([]);
  },
});

export { expect };

export function watchErrors(page: Page): string[] {
  const errors: string[] = [];
  page.on("console", (message) => {
    if (message.type() === "error") {
      errors.push(message.text());
    }
  });
  page.on("pageerror", (error) => errors.push(error.message));
  return errors;
}

/** Signs in as a new guest and enters a world; resolves once the room is showing. Returns the player's ID. */
export async function enterWorld(page: Page, world: string): Promise<string> {
  await page.goto("/");
  await page.getByRole("button", { name: "Play as a guest" }).click();
  await page.getByRole("button", { name: new RegExp(`^${world} · `) }).click();
  await expect(page.locator(".room-bar strong")).toHaveText("Town Square");
  return page.evaluate(async () => ((await (await fetch("/api/me")).json()) as { id: string }).id);
}

/** A second player, in their own browser context (their own cookie). */
export async function newPlayer(browser: Browser): Promise<{ page: Page; errors: string[]; close(): Promise<void> }> {
  const context = await browser.newContext();
  const page = await context.newPage();
  return { page, errors: watchErrors(page), close: () => context.close() };
}

/** The cats this page's room knows, by player ID (window.pspsps, client/src/main.ts). */
export function cats(page: Page): Promise<Record<string, { x: number; z: number }>> {
  return page.evaluate(() => Object.fromEntries(window.pspsps?.room()?.cats ?? []));
}

declare global {
  interface Window {
    pspsps?: {
      room():
        | { cats: Map<string, { x: number; z: number }>; chat: { playerId: string; text: string }[] }
        | undefined;
    };
  }
}

import { expect, test as base, type Browser, type Page } from "@playwright/test";
import { PNG } from "pngjs";

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

/** Each cat's latest emote in this page's room, by player ID. */
export function emotes(page: Page): Promise<Record<string, string>> {
  return page.evaluate(() =>
    Object.fromEntries([...(window.pspsps?.room()?.emotes ?? [])].map(([id, emote]) => [id, emote.emote])));
}

/** The cats this page's room knows, by player ID (window.pspsps, client/src/main.ts). */
export function cats(page: Page): Promise<Record<string, { x: number; z: number; pose: string | null }>> {
  return page.evaluate(() => Object.fromEntries(window.pspsps?.room()?.cats ?? []));
}

declare global {
  interface Window {
    pspsps?: {
      room():
        | {
          cats: Map<string, { x: number; z: number; pose: string | null }>;
          chat: { playerId: string; text: string }[];
          emotes: Map<string, { playerId: string; emote: string }>;
          snapshot: {
            worldId: string;
            roomId: string;
            doors: { id: string; x: number; z: number; radius: number; toRoomId: string }[];
          };
        }
        | undefined;
      coins(): number;
      fishing(): "idle" | "casting" | "waiting" | "bite" | "reeling";
      animation(playerId: string): string | undefined;
      screenPoint(x: number, z: number): { x: number; y: number } | undefined;
    };
  }
}

/** The animation a cat is playing on this page ("idle", "walk", ...), once it is drawn (window.pspsps). */
export function animation(page: Page, playerId: string): Promise<string | undefined> {
  return page.evaluate((id) => window.pspsps?.animation(id), playerId);
}

/** Town Square's pond water (#5fb3d9). */
export const pondBlue: [number, number, number] = [0x5f, 0xb3, 0xd9];

/** A cat's eyes (#ffc85a, client/src/cat.ts), whatever its fur: nothing else in a room is this yellow. */
export const catEyes: [number, number, number] = [0xff, 0xc8, 0x5a];

/** A door's mat (#c8553d, client/src/scene.ts). */
export const doorMatRed: [number, number, number] = [0xc8, 0x55, 0x3d];

/** How many pixels (every <step>th; by default a sample) are within a small distance of <rgb>. */
export function pixelsNear(png: Buffer, rgb: [number, number, number], step = 7): number {
  const image = PNG.sync.read(png);
  let near = 0;
  for (let i = 0; i < image.data.length; i += 4 * step) {
    const d = Math.abs(image.data[i]! - rgb[0]) + Math.abs(image.data[i + 1]! - rgb[1]) + Math.abs(image.data[i + 2]! - rgb[2]);
    if (d < 30) near++;
  }
  return near;
}

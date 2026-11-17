import { HubConnectionBuilder, LogLevel, type HubConnection } from "@microsoft/signalr";
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

/** Walks the player's cat through the room's door to <roomId> by clicking its mat where it is drawn; resolves in there. */
export async function goThroughDoor(page: Page, roomId: string, roomName: string): Promise<void> {
  const door = await page.evaluate((to) => window.pspsps?.room()?.snapshot.doors.find((d) => d.toRoomId === to), roomId);
  if (!door) throw new Error(`no door to ${roomId}`);
  const canvas = await page.locator("#game").boundingBox();
  if (!canvas) throw new Error("no canvas");
  const mat = await page.evaluate(({ x, z }) => window.pspsps?.screenPoint(x, z), door);
  if (!mat) throw new Error("the door isn't drawn");
  await page.mouse.click(canvas.x + mat.x, canvas.y + mat.y);
  await expect(page.locator(".room-bar strong")).toHaveText(roomName);
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
      catOnScreen(playerId: string): { x: number; y: number } | undefined;
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

/**
 * Clicks (or taps) a cat in the room, once it is drawn and stands still on screen: a cat that just walked is still on
 * its way on the canvas.
 */
export async function clickCat(page: Page, playerId: string): Promise<void> {
  // A cat takes taps once its model is drawn.
  await expect.poll(() => page.evaluate((id) => window.pspsps?.animation(id), playerId)).toBeDefined();
  let last: string | undefined;
  await expect.poll(async () => {
    const at = await page.evaluate((id) => window.pspsps?.catOnScreen(id), playerId);
    const now = at ? JSON.stringify(at) : undefined;
    const still = now !== undefined && now === last;
    last = now;
    return still;
  }, { intervals: [250] }).toBe(true);
  const at = await page.evaluate((id) => window.pspsps?.catOnScreen(id), playerId);
  const canvas = await page.locator("#game").boundingBox();
  if (!at || !canvas) throw new Error(`no cat ${playerId} on screen`);
  // A phone is tapped, as a player would.
  if (await page.evaluate(() => matchMedia("(pointer: coarse)").matches)) {
    await page.touchscreen.tap(canvas.x + at.x, canvas.y + at.y);
  } else {
    await page.mouse.click(canvas.x + at.x, canvas.y + at.y);
  }
}

/**
 * Another player without a browser: a new guest on the game's hub, from Node. For setting up a screen (a friend
 * elsewhere, a request waiting) without drawing a room for them. Close it after.
 */
export async function hubGuest(baseUrl: string): Promise<{ id: string; name: string; hub: HubConnection }> {
  const response = await fetch(new URL("/auth/guest", baseUrl), { method: "POST" });
  expect(response.ok).toBe(true);
  const cookie = response.headers.getSetCookie().find((c) => c.startsWith("pspsps="))?.split(";")[0];
  if (!cookie) throw new Error("no game cookie for the guest");
  const { id, displayName } = (await response.json()) as { id: string; displayName: string };
  const hub = new HubConnectionBuilder()
    .withUrl(new URL("/hubs/game", baseUrl).toString(), { headers: { Cookie: cookie } })
    .configureLogging(LogLevel.Warning)
    .build();
  await hub.start();
  return { id, name: displayName, hub };
}

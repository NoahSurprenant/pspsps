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

/**
 * Where the room's door to <roomId> has its mat drawn, in page pixels, or why it can't be clicked there: off the canvas,
 * or under a control drawn over it.
 */
export async function doorMat(page: Page, roomId: string): Promise<{ x: number; y: number } | string> {
  const door = await page.evaluate((to) => window.pspsps?.room()?.snapshot.doors.find((d) => d.toRoomId === to), roomId);
  if (!door) return `no door to ${roomId}`;
  const canvas = await page.locator("#game").boundingBox();
  if (!canvas) return "no canvas";
  const mat = await page.evaluate(({ x, z }) => window.pspsps?.screenPoint(x, z), door);
  if (!mat) return `the door to ${roomId} isn't drawn`;
  const point = { x: canvas.x + mat.x, y: canvas.y + mat.y };
  const on = await page.evaluate(({ x, y }) => {
    const top = document.elementFromPoint(x, y);
    return top?.id === "game" ? undefined : top ? `${top.tagName.toLowerCase()}.${top.className}` : "nothing";
  }, point);
  return on ? `the door to ${roomId}'s mat is under ${on}` : point;
}

/**
 * Walks the player's cat through the room's door to <roomId> by clicking (or, on a phone, tapping) its mat where it is
 * drawn, as a player would; resolves in there.
 */
export async function goThroughDoor(page: Page, roomId: string, roomName: string): Promise<void> {
  const mat = await doorMat(page, roomId);
  if (typeof mat === "string") throw new Error(mat);
  if (await page.evaluate(() => matchMedia("(pointer: coarse)").matches)) {
    await page.touchscreen.tap(mat.x, mat.y);
  } else {
    await page.mouse.click(mat.x, mat.y);
  }
  await expect(page.locator(".room-bar strong")).toHaveText(roomName);
}

/** The buttons in the room bar, by name. */
export function roomBarButtons(page: Page): Promise<string[]> {
  return page.locator(".room-bar button").allInnerTexts();
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
          cats: Map<string, { x: number; z: number; pose: string | null; hat: string | null; collar: string | null }>;
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
      wearing(playerId: string): { fur: string; hat: string | undefined; collar: string | undefined } | undefined;
      home(): { ownerId: string; visitors: string; furniture: { id: string; itemId: string; x: number; z: number }[] } | undefined;
      drawnProps(): readonly { kind: string; x: number; z: number }[];
      decorating(): { picked?: string; selected?: string } | undefined;
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
export async function hubGuest(baseUrl: string): Promise<{ id: string; name: string; cookie: string; hub: HubConnection }> {
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
  return { id, name: displayName, cookie, hub };
}

/**
 * Fishes at <world>'s Town Square pond from a hub guest (no browser) until it has at least <coins>, as a player would:
 * cast, wait for the bite (3-9 s, real time), reel at once. Returns the coins it has. About 14 coins a catch on average.
 */
export async function fishFor(hub: HubConnection, world: string, coins: number): Promise<number> {
  let bite: () => void = () => {};
  hub.on("FishBit", () => bite());
  await hub.invoke("EnterWorld", world);
  // The edge of Town Square's pond, at (-7, -3) with a radius of 2.5.
  await hub.invoke("Move", -7, 0);
  let total = 0;
  while (total < coins) {
    const bit = new Promise<void>((resolve, reject) => {
      const late = setTimeout(() => reject(new Error("nothing bit within 15 s")), 15_000);
      bite = () => {
        clearTimeout(late);
        resolve();
      };
    });
    await hub.invoke("Cast");
    await bit;
    const reel = await hub.invoke<{ outcome: string; totalCoins: number | null }>("Reel");
    total = reel.totalCoins ?? total;
    // A cat waits a second after reeling before it casts again.
    await new Promise((resolve) => setTimeout(resolve, 1_100));
  }
  return total;
}

/** Goes to the player's own home, from the friends list (where the way home is); resolves in there. */
export async function goHome(page: Page): Promise<void> {
  await page.getByRole("button", { name: "Friends", exact: true }).click();
  await page.getByRole("button", { name: "Go to your home" }).click();
  await expect(page.locator(".room-bar strong")).toHaveText(/'s home$/);
}

/**
 * Taps (or, with a mouse, clicks) the spot (x, z) on the room's floor where it is drawn, as a player would; fails when
 * it is off the canvas or under a control.
 */
export async function tapFloor(page: Page, x: number, z: number): Promise<void> {
  const canvas = await page.locator("#game").boundingBox();
  const at = await page.evaluate(([px, pz]) => window.pspsps?.screenPoint(px, pz), [x, z] as const);
  if (!canvas || !at) throw new Error("no room drawn");
  const point = { x: canvas.x + at.x, y: canvas.y + at.y };
  const on = await page.evaluate(({ x: px, y: py }) => document.elementFromPoint(px, py)?.id, point);
  if (on !== "game") throw new Error(`the floor at (${x}, ${z}) is under #${on ?? "nothing"}`);
  if (await page.evaluate(() => matchMedia("(pointer: coarse)").matches)) {
    await page.touchscreen.tap(point.x, point.y);
  } else {
    await page.mouse.click(point.x, point.y);
  }
}

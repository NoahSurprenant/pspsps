import { HubConnectionBuilder, LogLevel, type HubConnection } from "@microsoft/signalr";
import { expect, test as base, type Browser, type BrowserContext, type Locator, type Page } from "@playwright/test";
import { PNG } from "pngjs";

/** Fewer frames drawn: several pages drawing in software WebGL on one runner starve each other. */
export async function drawLess(context: BrowserContext): Promise<void> {
  await context.addInitScript(() => {
    if (localStorage.getItem("pspsps.frameRate") === null) localStorage.setItem("pspsps.frameRate", "30");
  });
}

/**
 * Holds a page's drawing (its animation frames are kept, not run) while <during> runs, then lets it go on. Pages already
 * in a room draw in software WebGL on the same runner CPUs as a page that is joining: a third page then stalls for
 * seconds building its 3D scene, so the joined pages stand still until it has its room.
 */
export async function holdDrawing<T>(pages: Page[], during: () => Promise<T>): Promise<T> {
  await Promise.all(pages.map((page) => page.evaluate(() => {
    const held: FrameRequestCallback[] = [];
    const real = window.requestAnimationFrame.bind(window);
    (window as unknown as { releaseDrawing: () => void }).releaseDrawing = () => {
      window.requestAnimationFrame = real;
      for (const frame of held.splice(0)) real(frame);
    };
    window.requestAnimationFrame = (frame) => held.push(frame);
  })));
  try {
    return await during();
  } finally {
    await Promise.all(pages.map((page) => page.evaluate(() =>
      (window as unknown as { releaseDrawing: () => void }).releaseDrawing())));
  }
}

/** A page that fails its test on any console error or uncaught exception (the "No camera defined" kind). */
export const test = base.extend<{ page: Page }>({
  page: async ({ page }, use) => {
    await drawLess(page.context());
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

/**
 * Waits until the page shows the room <name>: by the page's title ("Cat Café · pspsps"), since the room bar has no room
 * name, and the room itself (window.pspsps).
 */
export async function expectRoom(page: Page, name: string): Promise<void> {
  try {
    await expect(page).toHaveTitle(`${name} · pspsps`);
    await expect.poll(() => page.evaluate(() => window.pspsps?.room()?.snapshot.roomName)).toBe(name);
  } catch (error) {
    await attachPage(page, `not-in-${name}`);
    throw error;
  }
}

/** Attaches what a page shows (screenshot, page text, title, room) to the test, whichever player's page it is. */
export async function attachPage(page: Page, label: string): Promise<void> {
  const info = base.info();
  try {
    const state = await page.evaluate(() => ({
      url: location.href,
      title: document.title,
      room: window.pspsps?.room()?.snapshot.roomName,
      text: document.body.innerText,
    }));
    await info.attach(`${label}.json`, { body: JSON.stringify(state, null, 2), contentType: "application/json" });
    await info.attach(`${label}.png`, { body: await page.screenshot(), contentType: "image/png" });
  } catch (inner) {
    await info.attach(`${label}-unavailable.txt`, { body: String(inner), contentType: "text/plain" });
  }
}

/** Signs in as a new guest and enters a world; resolves once the room is showing. Returns the player's ID. */
export async function enterWorld(page: Page, world: string): Promise<string> {
  await page.goto("/");
  await page.getByRole("button", { name: "Play as a guest" }).click();
  await page.getByRole("button", { name: new RegExp(`^${world} · `) }).click();
  await expectRoom(page, "Town Square");
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
  // A cat arrives beside the door it came through, drawn over the mat, and a click on a cat opens its card instead of
  // walking: so the mat is clicked where no cat is drawn over it, at its middle or toward either side.
  const spots = await page.evaluate(({ x, z, radius, edge }) => {
    const along = edge === "north" || edge === "south" ? { x: radius * 0.8, z: 0 } : { x: 0, z: radius * 0.8 };
    const mat = (dx: number, dz: number) => window.pspsps?.screenPoint(x + dx, z + dz);
    const cats = [...(window.pspsps?.room()?.cats.keys() ?? [])].map((id) => window.pspsps?.catOnScreen(id)).filter((p) => p !== undefined);
    return [mat(0, 0), mat(along.x, along.z), mat(-along.x, -along.z)].filter((p) => p !== undefined)
      .map((p) => ({ ...p, clear: cats.every((cat) => Math.hypot(cat.x - p.x, cat.y - p.y) > 60) }));
  }, door);
  const chosen = spots.find((spot) => spot.clear) ?? spots[0];
  if (!chosen) return `the door to ${roomId} isn't drawn`;
  const point = { x: canvas.x + chosen.x, y: canvas.y + chosen.y };
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
  const touch = await page.evaluate(() => matchMedia("(pointer: coarse)").matches);
  let mat = await doorMat(page, roomId);
  if (typeof mat === "string" && touch) {
    await pinchRoom(page, "out");
    mat = await doorMat(page, roomId);
  }
  // A click that lands as the cat is still arriving, or on the cat, doesn't walk it: so it clicks again where the mat is
  // drawn now, and each miss is attached (screenshot and where everything was) to show why.
  for (let attempt = 1; ; attempt++) {
    if (typeof mat === "string") throw new Error(mat);
    if (touch) {
      await page.touchscreen.tap(mat.x, mat.y);
    } else {
      await page.mouse.click(mat.x, mat.y);
    }
    try {
      await expect(page).toHaveTitle(`${roomName} · pspsps`, { timeout: attempt < 3 ? 8_000 : 15_000 });
      break;
    } catch (error) {
      await attachMiss(page, roomId, attempt, mat);
      if (attempt >= 3) throw error;
      // A click that landed on the player's own cat opened its card, which now covers the mat.
      await page.keyboard.press("Escape");
      mat = await doorMat(page, roomId);
    }
  }
  await expectRoom(page, roomName);
}

async function attachMiss(page: Page, roomId: string, attempt: number, clicked: unknown): Promise<void> {
  const where = await page.evaluate(() => ({
    title: document.title,
    room: window.pspsps?.room()?.snapshot.roomName,
    cats: [...(window.pspsps?.room()?.cats.keys() ?? [])].map((id) => ({ id, ...window.pspsps?.catOnScreen(id) })),
  }));
  const info = base.info();
  await info.attach(`door-to-${roomId}-miss-${attempt}.png`, { body: await page.screenshot(), contentType: "image/png" });
  await info.attach(`door-to-${roomId}-miss-${attempt}.json`, {
    body: JSON.stringify({ clicked, ...where }, null, 2), contentType: "application/json",
  });
}

/** Pinches on the game canvas using touch input in Chromium. */
export async function pinchRoom(page: Page, zoom: "in" | "out"): Promise<void> {
  const bounds = await page.locator("#game").boundingBox();
  if (!bounds) throw new Error("no canvas to pinch");
  const x = bounds.x + bounds.width / 2;
  const y = bounds.y + bounds.height / 2;
  const points = (fraction: number) => [
    { id: 1, x: x - fraction * bounds.width, y },
    { id: 2, x: x + fraction * bounds.width, y },
  ];
  const session = await page.context().newCDPSession(page);
  try {
    const spread = points(0.26);
    const together = points(0.055);
    await session.send("Input.dispatchTouchEvent", { type: "touchStart", touchPoints: zoom === "out" ? spread : together });
    await session.send("Input.dispatchTouchEvent", { type: "touchMove", touchPoints: zoom === "out" ? together : spread });
    await session.send("Input.dispatchTouchEvent", { type: "touchEnd", touchPoints: [] });
  } finally {
    await session.detach();
  }
}

/** The buttons in the room bar, by name. */
export function roomBarButtons(page: Page): Promise<string[]> {
  return page.locator(".room-bar button").evaluateAll((buttons) =>
    buttons.map((button) => button.getAttribute("aria-label") ?? button.textContent ?? ""));
}

/** A second player, in their own browser context (their own cookie). */
export async function newPlayer(
  browser: Browser, from?: string,
): Promise<{ page: Page; errors: string[]; close(): Promise<void> }> {
  // From the address <from>, if given (forwarded: see fakeNetwork).
  const context = await browser.newContext(from ? { extraHTTPHeaders: { "X-Forwarded-For": from } } : {});
  await drawLess(context);
  const page = await context.newPage();
  return { page, errors: watchErrors(page), close: () => context.close() };
}

/** Each cat's latest emote in this page's room, by player ID. */
export function emotes(page: Page): Promise<Record<string, string>> {
  return page.evaluate(() =>
    Object.fromEntries([...(window.pspsps?.room()?.emotes ?? [])].map(([id, emote]) => [id, emote.emote])));
}

/** The cats this page's room knows, by player ID (window.pspsps, client/src/main.ts). */
export function cats(
  page: Page,
): Promise<Record<string, { x: number; z: number; pose: string | null; displayName: string }>> {
  return page.evaluate(() => Object.fromEntries(window.pspsps?.room()?.cats ?? []));
}

declare global {
  interface Window {
    pspsps?: {
      room():
        | {
          cats: Map<
            string,
            { x: number; z: number; pose: string | null; hat: string | null; collar: string | null; displayName: string }
          >;
          chat: { playerId: string; text: string }[];
          emotes: Map<string, { playerId: string; emote: string }>;
          snapshot: {
            worldId: string;
            worldMap?: { rooms: { id: string; width: number; depth: number; x: number; z: number }[] };
            roomId: string;
            roomName: string;
            props: { kind: string; x: number; z: number; radius: number }[] | null;
            doors: { id: string; x: number; z: number; radius: number; toRoomId: string; edge?: string }[];
          };
        }
        | undefined;
      coins(): number;
      fishing(): "idle" | "casting" | "waiting" | "bite" | "reeling";
      animation(playerId: string): string | undefined;
      screenPoint(x: number, z: number): { x: number; y: number } | undefined;
      catDrawnAt(playerId: string): { x: number; z: number } | undefined;
      catOnScreen(playerId: string): { x: number; y: number } | undefined;
      wearing(playerId: string): { fur: string; hat: string | undefined; collar: string | undefined } | undefined;
      home(): { ownerId: string; visitors: string; furniture: { id: string; itemId: string; x: number; z: number }[] } | undefined;
      drawnProps(): readonly { kind: string; x: number; z: number }[];
      decorating(): { picked?: string; selected?: string } | undefined;
      mutes(): string[];
      bubble(playerId: string): string | undefined;
      portrait(outfit: { fur: string; hat: string | null; collar: string | null }): Promise<string> | undefined;
      wardrobe(): Promise<{ hats: readonly string[]; collars: readonly string[] }>;
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
export const pathTan: [number, number, number] = [0xd9, 0xa4, 0x68];

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
  // Still on screen is not enough: a page that has stopped drawing for a moment shows a walking cat still. It is
  // standing once it is drawn where the room says it is going.
  await expect.poll(() => page.evaluate((id) => {
    const goal = window.pspsps?.room()?.cats.get(id);
    const drawn = window.pspsps?.catDrawnAt(id);
    return goal !== undefined && drawn !== undefined && Math.hypot(goal.x - drawn.x, goal.z - drawn.z) < 0.05;
  }, playerId)).toBe(true);
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
/**
 * A made-up address (an IPv6 /64 of the documentation range, new each time) for a player to come from: CI's server sees
 * one address, its own, for every browser, and trusts a forwarded one from there (loopback, as the framework does by
 * default), as it would from its proxy. A network ban on CI's own address would lock the whole run out; on one of these,
 * nobody else.
 */
export function fakeNetwork(): string {
  const part = () => Math.floor(Math.random() * 0x10000).toString(16);
  return `2001:db8:${part()}:${part()}::1`;
}

export async function hubGuest(
  baseUrl: string, from?: string,
): Promise<{ id: string; name: string; cookie: string; hub: HubConnection }> {
  const forwarded: Record<string, string> = from ? { "X-Forwarded-For": from } : {};
  const response = await fetch(new URL("/auth/guest", baseUrl), { method: "POST", headers: forwarded });
  expect(response.ok).toBe(true);
  const cookie = response.headers.getSetCookie().find((c) => c.startsWith("pspsps="))?.split(";")[0];
  if (!cookie) throw new Error("no game cookie for the guest");
  const { id, displayName } = (await response.json()) as { id: string; displayName: string };
  return { id, name: displayName, cookie, hub: await hubWith(baseUrl, cookie, from) };
}

/** A hub connection with the game cookie <cookie> ("pspsps=..."): a tab of that player, from Node. Stop it after. */
export async function hubWith(baseUrl: string, cookie: string, from?: string): Promise<HubConnection> {
  const hub = new HubConnectionBuilder()
    .withUrl(new URL("/hubs/game", baseUrl).toString(), {
      headers: { Cookie: cookie, ...(from ? { "X-Forwarded-For": from } : {}) },
    })
    .configureLogging(LogLevel.Warning)
    .build();
  await hub.start();
  return hub;
}

/**
 * Another tab of the player signed in on <page>, from Node, which doesn't play (it enters no world, so the page's tab
 * plays on): for changing what the player has from elsewhere, as their other tab or device would. Stop it after.
 */
export async function otherTab(page: Page, baseUrl: string): Promise<HubConnection> {
  const cookie = (await page.context().cookies(baseUrl)).find((c) => c.name === "pspsps");
  if (!cookie) throw new Error("the page has no game cookie");
  return hubWith(baseUrl, `pspsps=${cookie.value}`);
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
  await page.getByRole("button", { name: "Go home", exact: true }).click();
  await expect(page).toHaveTitle(/'s home · pspsps$/);
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

/** Where the shop's catalogue stand is drawn, in page pixels, or why it can't be tapped there. */
export async function catalogueStand(page: Page): Promise<{ x: number; y: number } | string> {
  const stand = await page.evaluate(() => window.pspsps?.room()?.snapshot.props?.find((p) => p.kind === "catalogue-stand"));
  if (!stand) return "no catalogue stand";
  const canvas = await page.locator("#game").boundingBox();
  const at = await page.evaluate(({ x, z }) => window.pspsps?.screenPoint(x, z), stand);
  if (!canvas || !at) return "the catalogue stand isn't drawn";
  const point = { x: canvas.x + at.x, y: canvas.y + at.y };
  const on = await page.evaluate(({ x, y }) => {
    const top = document.elementFromPoint(x, y);
    return top?.id === "game" ? undefined : top ? `${top.tagName.toLowerCase()}.${top.className}` : "nothing";
  }, point);
  return on ? `the catalogue stand is under ${on}` : point;
}

/**
 * Taps (or clicks) the shop's catalogue stand where it is drawn, as a player would: the cat walks up to it and the
 * catalogue opens. Resolves with the catalogue open.
 */
export async function openCatalogue(page: Page): Promise<void> {
  const stand = await catalogueStand(page);
  if (typeof stand === "string") throw new Error(stand);
  if (await page.evaluate(() => matchMedia("(pointer: coarse)").matches)) {
    await page.touchscreen.tap(stand.x, stand.y);
  } else {
    await page.mouse.click(stand.x, stand.y);
  }
  await expect(page.getByRole("dialog", { name: "Catalogue" })).toBeVisible({ timeout: 15_000 });
}

/** The party hat's pink (client/src/wearables.ts): nothing else on a card is this colour. */
export const partyHatPink: [number, number, number] = [0xff, 0x7e, 0xb6];

/** Waits for the picture of the cat on the card open to be taken and shown. */
export async function portraitShown(page: Page): Promise<void> {
  const image = page.locator("dialog .portrait img");
  await expect(image).toHaveAttribute("src", /^data:image\/png/);
  await expect.poll(() => image.evaluate((img: HTMLImageElement) => img.complete && img.naturalWidth > 0)).toBe(true);
}

/** The picture of the cat on the card open, once it has been taken. */
export async function portrait(page: Page): Promise<Buffer> {
  await portraitShown(page);
  return page.locator("dialog .portrait img").screenshot({ timeout: 15_000 });
}

/**
 * Presses Tab until <target> (one of the room's buttons for the keyboard, unseen until focused) has the focus, as a
 * keyboard player would; it shows then.
 */
export async function focusByTab(page: Page, target: Locator): Promise<void> {
  for (let tab = 0; tab < 30 && !(await target.evaluate((element) => element === document.activeElement)); tab++) {
    await page.keyboard.press("Tab");
  }
  await expect(target).toBeFocused();
  await expect(target).toBeVisible();
}

/**
 * The game's admin in CI: a guest made before the server started for the tests, whose player ID is in the server's
 * admin list (Moderation__AdminPlayerIds__0, as production lists its admins), and its cookie (E2E_ADMIN_COOKIE,
 * "pspsps=..."). Without one (a local run against a server with no admins) the test is skipped; in CI it must be there.
 */
export function adminCookie(): { name: string; value: string } {
  const cookie = process.env.E2E_ADMIN_COOKIE;
  if (!cookie) {
    if (process.env.CI) throw new Error("E2E_ADMIN_COOKIE isn't set: CI starts the server with an admin");
    test.skip(true, "no admin: set E2E_ADMIN_COOKIE to an admin's game cookie");
    throw new Error("skipped");
  }
  const at = cookie.indexOf("=");
  return { name: cookie.slice(0, at), value: cookie.slice(at + 1) };
}

/**
 * Bans <playerId> for a day, and every network its card shows, as the admin, from Node (the admin's own hub connection):
 * returns the networks' labels.
 */
export async function banWithNetworks(baseUrl: string, playerId: string, reason: string): Promise<string[]> {
  const { name, value } = adminCookie();
  const hub = await hubWith(baseUrl, `${name}=${value}`);
  try {
    type Card = { moderation: { networks: { key: string; label: string }[] | null } | null };
    const card = await hub.invoke<Card>("Moderate", playerId, "ban", reason, "24h");
    const networks = card.moderation?.networks ?? [];
    expect(networks.length, "the cat's networks on its card").toBeGreaterThan(0);
    await hub.invoke("BanNetworks", playerId, networks.map((n) => n.key), "24h");
    return networks.map((n) => n.label);
  } finally {
    await hub.stop();
  }
}

/** Signs <context>'s pages in as the game's admin (adminCookie). */
export async function signInAsAdmin(context: BrowserContext, baseUrl: string): Promise<void> {
  await context.addCookies([{ ...adminCookie(), url: baseUrl }]);
}

/** Enters <world> as the player <page> is signed in as already (no guest made): resolves once the room shows. */
export async function enterWorldSignedIn(page: Page, world: string): Promise<string> {
  await page.goto("/");
  await page.getByRole("button", { name: new RegExp(`^${world} · `) }).click();
  await expectRoom(page, "Town Square");
  return page.evaluate(async () => ((await (await fetch("/api/me")).json()) as { id: string }).id);
}

/**
 * An admin's action on a player, from a page of the game signed in as the admin (the admin page's own request, which
 * the browser marks as from this site).
 */
export async function moderate(
  page: Page, playerId: string, request: { action: string; reason?: string; length?: string },
): Promise<void> {
  const status = await page.evaluate(async ([id, body]) => (await fetch(`/api/admin/players/${id}/moderate`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  })).status, [playerId, request] as const);
  expect(status, `moderating ${playerId}`).toBe(200);
}

/** What window.pspsps.portrait pictures: a cat in a fur, with a hat and a collar (or none). */
export interface Outfit {
  fur: string;
  hat: string | null;
  collar: string | null;
}

/** A picture of a cat dressed in <outfit>, as the player cards picture it, as a PNG. */
export async function pictureOf(page: Page, outfit: Outfit): Promise<Buffer> {
  const url = await page.evaluate((o) => window.pspsps?.portrait(o), outfit) ?? "";
  expect(url, `a picture of ${JSON.stringify(outfit)}`).toMatch(/^data:image\/png;base64,/);
  return Buffer.from(url.slice(url.indexOf(",") + 1), "base64");
}

/** How many of a PNG's pixels differ from another's of the same size: what a hat or a collar adds to a picture. */
export function differing(a: Buffer, b: Buffer): number {
  const [one, two] = [PNG.sync.read(a), PNG.sync.read(b)];
  if (one.width !== two.width || one.height !== two.height) {
    // Read past the shorter buffer otherwise: undefined bytes make every difference read as 0, undercounting rather
    // than failing loudly.
    throw new Error(`differing() needs two pictures the same size, not ${one.width}x${one.height} and ${two.width}x${two.height}`);
  }

  let count = 0;
  for (let i = 0; i < one.data.length; i += 4) {
    const d = Math.abs(one.data[i]! - two.data[i]!) + Math.abs(one.data[i + 1]! - two.data[i + 1]!)
      + Math.abs(one.data[i + 2]! - two.data[i + 2]!) + Math.abs(one.data[i + 3]! - two.data[i + 3]!);
    if (d > 40) count++;
  }
  return count;
}

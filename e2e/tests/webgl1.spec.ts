import type { Page } from "@playwright/test";
import { PNG } from "pngjs";
import {
  catEyes, clickCat, doorMatRed, enterWorld, expect, goHome, goThroughDoor, pixelsNear, pondBlue, portrait, test,
} from "./game";

// The game on a device with WebGL1 only (older phones and tablets): the browser has no WebGL2, so Babylon falls back to
// WebGL1, where a texture whose sides aren't powers of two has no mipmaps and samples black. Every room's painted floor
// and walls, its pond and door mats, the cats, what they wear and the card's picture must still draw, with no errors
// (the test's page fails on any).

test.beforeEach(async ({ page }) => {
  // No WebGL2, as on such a device: asked for, the canvas has none to give.
  await page.addInitScript(() => {
    const getContext = HTMLCanvasElement.prototype.getContext as (this: HTMLCanvasElement, type: string, ...rest: unknown[]) => unknown;
    HTMLCanvasElement.prototype.getContext = function (this: HTMLCanvasElement, type: string, ...rest: unknown[]) {
      return type === "webgl2" || type === "experimental-webgl2" ? null : getContext.call(this, type, ...rest);
    } as typeof HTMLCanvasElement.prototype.getContext;
  });
});

/** Waits until the room's canvas, the controls hidden, shows each of <things> (what, its colour, the fewest pixels). */
async function expectDrawn(page: Page, things: [string, [number, number, number], number][]): Promise<void> {
  await page.locator("#ui").evaluate((ui) => (ui.style.visibility = "hidden"));
  try {
    for (const [what, rgb, least] of things) {
      await expect.poll(async () => pixelsNear(await page.locator("#game").screenshot(), rgb), { message: what, timeout: 15_000 })
        .toBeGreaterThan(least);
    }
  } finally {
    await page.locator("#ui").evaluate((ui) => (ui.style.visibility = ""));
  }
}

/** How many of a picture's pixels differ from another's. */
function differing(a: Buffer, b: Buffer): number {
  const [one, two] = [PNG.sync.read(a), PNG.sync.read(b)];
  let count = 0;
  for (let i = 0; i < one.data.length; i += 4) {
    const d = Math.abs(one.data[i]! - two.data[i]!) + Math.abs(one.data[i + 1]! - two.data[i + 1]!)
      + Math.abs(one.data[i + 2]! - two.data[i + 2]!) + Math.abs(one.data[i + 3]! - two.data[i + 3]!);
    if (d > 40) count++;
  }
  return count;
}

async function pictureOf(page: Page, outfit: { fur: string; hat: string | null; collar: string | null }): Promise<Buffer> {
  const url = await page.evaluate((o) => window.pspsps?.portrait(o), outfit);
  expect(url).toMatch(/^data:image\/png;base64,/);
  return Buffer.from((url ?? "").slice((url ?? "").indexOf(",") + 1), "base64");
}

test("every room, the cats, what they wear and the card's picture draw with WebGL1 only", async ({ page }) => {
  test.setTimeout(180_000);
  const me = await enterWorld(page, "Tabby");

  // The game's canvas has a WebGL1 context: asked for one again it hands back the same, and WebGL2 it never had.
  const version = await page.locator("#game").evaluate((canvas: HTMLCanvasElement) => {
    const gl = canvas.getContext("webgl");
    return gl === null ? "none" : typeof WebGL2RenderingContext !== "undefined" && gl instanceof WebGL2RenderingContext ? "2" : "1";
  });
  expect(version).toBe("1");

  // Town Square: its painted floor's pond, the doors' mats, and the cat (its eyes).
  await expectDrawn(page, [["the pond", pondBlue, 20], ["the doors' mats", doorMatRed, 4], ["the cat", catEyes, 0]]);

  // The café and the shop, through their doors, and back.
  await goThroughDoor(page, "cafe", "Cat Café");
  await expectDrawn(page, [["the café's door mat", doorMatRed, 2], ["the cat", catEyes, 0]]);
  await goThroughDoor(page, "town-square", "Town Square");
  await goThroughDoor(page, "shop", "Cat Shop");
  await expectDrawn(page, [["the shop's door mat", doorMatRed, 2], ["the cat", catEyes, 0]]);

  // The player's home.
  await goHome(page);
  await expectDrawn(page, [["the home's door mat", doorMatRed, 2], ["the cat", catEyes, 0]]);

  // The player's own card, and its picture of the cat.
  await clickCat(page, me);
  const picture = await portrait(page);
  expect(pixelsNear(picture, catEyes, 1), "the cat in the card's picture").toBeGreaterThan(50);
  await page.keyboard.press("Escape");

  // A hat and a collar, drawn on a cat as the cards picture it.
  const bare = await pictureOf(page, { fur: "Tuxedo", hat: null, collar: null });
  for (const [hat, collar] of [["top-hat", "bell-collar"], ["beanie", "red-collar"], ["crown", "blue-collar"]] as const) {
    const dressed = await pictureOf(page, { fur: "Tuxedo", hat, collar });
    expect(differing(dressed, bare), `${hat} and ${collar}`).toBeGreaterThan(400);
  }
});

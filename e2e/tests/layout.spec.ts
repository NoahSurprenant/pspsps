import { mkdir } from "node:fs/promises";
import { join } from "node:path";
import { expect, test, type Page } from "@playwright/test";
import { catalogueStand, doorMat, doorMatRed, pathTan, pinchRoom, pixelsNear, pondBlue, watchErrors } from "./game";
import { contextFor, SCREENS, type Screen, type Size } from "./screens";

// Every screen at every size: every control and line of text can be reached (on screen, or in a panel that scrolls),
// every part is on screen and none sits on another, and on phones the controls are big enough to tap and the text
// boxes big enough not to zoom the page when tapped.
//
// Each screen and size is screenshotted to screenshots/<screen>/<n>-<device>-<width>x<height>.png; CI puts each screen's
// sizes side by side (contact-sheets.mjs) and links them on the PR.

/** The screen sizes, by device: the name goes in each screenshot's file name, which the contact sheets order by. */
const SIZES: Size[] = [
  { device: "desktop", width: 1920, height: 1080, phone: false },
  { device: "laptop", width: 1280, height: 720, phone: false },
  { device: "tablet-landscape", width: 1024, height: 768, phone: false },
  { device: "tablet-portrait", width: 768, height: 1024, phone: false },
  // A phone on its side, with the browser's bars taking some of the height.
  { device: "phone-landscape", width: 844, height: 390, phone: true },
  { device: "phone-portrait", width: 390, height: 844, phone: true },
  { device: "small-phone", width: 360, height: 640, phone: true },
  // The narrowest phone in use: a folding phone, folded.
  { device: "fold-phone", width: 280, height: 653, phone: true },
];

/** Apple's minimum for a tap target (Google's is 48 dp); WCAG 2.2 AA asks 24 CSS px. Phones are held to the larger. */
const MIN_TAP = 44;
/** iOS Safari zooms the page into a text box whose font is smaller than this. */
const MIN_INPUT_FONT = 16;

interface Checks {
  /** The parts that must each be wholly on screen and not on top of one another. */
  parts: string[];
  /** Anything else the screen must show at every size, waited for before the screenshot: what is missing. */
  shows?(page: Page): Promise<string[]>;
  /** Anything else that must be reachable with the controls showing: what isn't. */
  reachable?(page: Page): Promise<string[]>;
}

const CHECKS: Record<Screen["name"], Checks> = {
  "sign-in": { parts: [".panel"] },
  worlds: { parts: [".panel"] },
  // A sheet over the room: the room's parts are behind it (inert), so only the sheet itself must fit.
  "player-card": { parts: [".sheet"] },
  report: { parts: [".sheet"] },
  "own-card": { parts: [".sheet"] },
  "own-card-rename": { parts: [".sheet"] },
  friends: { parts: [".sheet"] },
  catalogue: { parts: [".sheet"] },
  // An admin's view of another cat's card, Moderate open: still a sheet that fits (and scrolls).
  "admin-card": { parts: [".sheet"] },
  // The admin page: its own page, which scrolls inside its column.
  "admin-page": { parts: [".admin"] },
  "world-map": { parts: [".sheet"], shows: mapLabelsFit },
  "town-square": {
    parts: [".room-bar", ".emote-bar", ".chat-bar", ".fishing"],
    // The pond and the doors' mats, drawn: from further off (a narrow screen backs the camera away) the floor's outline
    // once hid the pond, and props must never cover them.
    shows: (page) => drawn(page, [["the pond", pondBlue, 20], ["the doors' mats", doorMatRed, 4], ["the paths", pathTan, 4]]),
    reachable: (page) => doorsReachable(page, ["cafe", "shop", "garden", "riverside"]),
  },
  // A home: its owner's controls (Decorate, who can visit) in the dock, and its door out to Town Square clickable.
  home: {
    parts: [".room-bar", ".home-tools", ".emote-bar", ".chat-bar"],
    shows: (page) => drawn(page, [["the door's mat", doorMatRed, 2]]),
    reachable: async (page) => {
      const mat = await doorMat(page, "town-square");
      return typeof mat === "string" ? [mat] : [];
    },
  },
  // Decorating: the panel in place of the emotes and the chat, the room above it.
  decorating: { parts: [".room-bar", ".decorate"] },
  // The shop: its door back to the square drawn and clickable, and its catalogue stand clickable too.
  "shop-room": {
    parts: [".room-bar", ".emote-bar", ".chat-bar"],
    shows: (page) => drawn(page, [["the door's mat", doorMatRed, 2]]),
    reachable: async (page) => {
      const [mat, stand] = [await doorMat(page, "town-square"), await catalogueStand(page)];
      return [mat, stand].filter((thing): thing is string => typeof thing === "string");
    },
  },
  cafe: {
    parts: [".room-bar", ".emote-bar", ".chat-bar"],
    // The door back to the square's mat, among the café's tables and chairs, drawn and clickable.
    shows: (page) => drawn(page, [["the door's mat", doorMatRed, 2]]),
    reachable: (page) => doorsReachable(page, ["town-square"]),
  },
  garden: {
    parts: [".room-bar", ".emote-bar", ".chat-bar"],
    shows: (page) => drawn(page, [["the paths", pathTan, 4]]),
    reachable: (page) => doorsReachable(page, ["town-square", "riverside"]),
  },
  riverside: {
    parts: [".room-bar", ".emote-bar", ".chat-bar", ".fishing"],
    shows: (page) => drawn(page, [["the pond", pondBlue, 20], ["the paths", pathTan, 4]]),
    reachable: (page) => doorsReachable(page, ["garden", "town-square"]),
  },
};

async function doorsReachable(page: Page, destinations: string[]): Promise<string[]> {
  const mats = await Promise.all(destinations.map((destination) => doorMat(page, destination)));
  return mats.filter((mat): mat is string => typeof mat === "string");
}

async function mapLabelsFit(page: Page): Promise<string[]> {
  return page.locator(".world-map").evaluate((map) => {
    const problems: string[] = [];
    const bounds = map.getBoundingClientRect();
    const buttons = [...map.querySelectorAll<HTMLButtonElement>(".map-place")];
    for (const [index, button] of buttons.entries()) {
      const name = button.textContent?.trim();
      const box = button.getBoundingClientRect();
      const label = button.querySelector("span:last-child");
      if (box.left < bounds.left || box.right > bounds.right || box.top < bounds.top || box.bottom > bounds.bottom) {
        problems.push(`${name} extends past the map`);
      }
      if (label && label.scrollWidth > label.clientWidth + 1) {
        problems.push(`${name} overflows its button`);
      }
      for (const other of buttons.slice(index + 1)) {
        const b = other.getBoundingClientRect();
        if (box.left < b.right && b.left < box.right && box.top < b.bottom && b.top < box.bottom) {
          problems.push(`${name} overlaps ${other.textContent?.trim()}`);
        }
      }
    }
    return problems;
  });
}

/**
 * Which of <things> (what, its colour, the fewest sampled pixels of it) are not drawn on the canvas. Waited for, as
 * software WebGL can be slow to draw the first frames, and seen with the controls hidden.
 */
async function drawn(page: Page, things: [string, [number, number, number], number][]): Promise<string[]> {
  await page.locator("#ui").evaluate((ui) => (ui.style.visibility = "hidden"));
  try {
    const missing: string[] = [];
    for (const [what, rgb, least] of things) {
      try {
        await expect.poll(async () => pixelsNear(await page.locator("#game").screenshot(), rgb), { timeout: 15_000 })
          .toBeGreaterThan(least);
      } catch {
        missing.push(`${what} is not drawn`);
      }
    }
    return missing;
  } finally {
    await page.locator("#ui").evaluate((ui) => (ui.style.visibility = ""));
  }
}

for (const screen of SCREENS) {
  const checks = CHECKS[screen.name];
  for (const [index, size] of SIZES.entries()) {
    test(`${screen.name} at ${size.device} ${size.width}x${size.height}`, async ({ browser }) => {
      const context = await contextFor(browser, size);
      let close: (() => Promise<void>) | undefined;
      try {
        const page = await context.newPage();
        const errors = watchErrors(page);
        close = (await screen.open(page)) ?? undefined;
        // Check and capture every exit in the whole-room view. The portrait zoom has its own gesture test.
        if (checks.reachable && size.phone && size.width < size.height) {
          await pinchRoom(page, "out");
        }
        const missing = checks.shows ? await checks.shows(page) : [];

        const dir = join("screenshots", screen.name);
        await mkdir(dir, { recursive: true });
        await page.screenshot({ path: join(dir, `${index + 1}-${size.device}-${size.width}x${size.height}.png`) });

        const problems = [
          ...missing,
          ...(checks.reachable ? await checks.reachable(page) : []),
          ...await offScreen(page),
          ...await offScreenOrOverlapping(page, checks.parts),
          ...(size.phone ? await smallTapTargets(page) : []),
          ...(size.phone ? await smallInputs(page) : []),
          ...await squashedPortraits(page),
        ];
        expect(problems, `${screen.name} at ${size.width}x${size.height}`).toEqual([]);
        expect(errors, "console errors and uncaught exceptions").toEqual([]);
      } finally {
        await close?.();
        await context.close();
      }
    });
  }
}

/** How far past an edge a box may sit and still count as on screen: sub-pixel rounding of centred parts. */
const SLACK = 0.5;
/**
 * Every control and line of text must be wholly on screen, unless a panel it is in scrolls to it. The page itself never
 * scrolls (the UI is fixed over the canvas), so anything else past an edge can't be reached at all.
 */
async function offScreen(page: Page): Promise<string[]> {
  return page.evaluate((slack) => [...document.querySelectorAll<HTMLElement>("#ui button, #ui input, #ui h1, #ui p, #ui strong, #ui span")]
    .filter((element) => element.offsetParent !== null)
    .filter((element) => {
      for (let parent = element.parentElement; parent && parent.id !== "ui"; parent = parent.parentElement) {
        if (["auto", "scroll"].includes(getComputedStyle(parent).overflowY)) return false;
      }
      return true;
    })
    .map((element) => ({ element, r: element.getBoundingClientRect() }))
    .filter(({ r }) => r.left < -slack || r.top < -slack || r.right > window.innerWidth + slack || r.bottom > window.innerHeight + slack)
    .map(({ element, r }) => `"${element.textContent?.trim() || element.getAttribute("placeholder") || element.tagName}" is partly off screen (${[r.left, r.top, r.right, r.bottom].map(Math.round).join(", ")})`), SLACK);
}

/** Each part that exists must be wholly inside the viewport, and no two may overlap. */
async function offScreenOrOverlapping(page: Page, parts: string[]): Promise<string[]> {
  const boxes = await page.evaluate((selectors) => selectors.flatMap((selector) => {
    const element = document.querySelector(selector);
    if (!element) return [];
    const r = element.getBoundingClientRect();
    return [{ selector, left: r.left, top: r.top, right: r.right, bottom: r.bottom }];
  }), parts);
  const { width, height } = page.viewportSize() ?? { width: 0, height: 0 };
  const problems: string[] = [];
  for (const box of boxes) {
    if (box.left < -SLACK || box.top < -SLACK || box.right > width + SLACK || box.bottom > height + SLACK) {
      problems.push(`${box.selector} is partly off screen (${round(box)})`);
    }
  }
  for (let i = 0; i < boxes.length; i++) {
    for (let j = i + 1; j < boxes.length; j++) {
      const a = boxes[i]!;
      const b = boxes[j]!;
      if (a.left < b.right && b.left < a.right && a.top < b.bottom && b.top < a.bottom) {
        problems.push(`${a.selector} overlaps ${b.selector}`);
      }
    }
  }
  return problems;
}

/** The smallest a card's picture of the cat may be drawn, in CSS px (it is min(8.5rem, 40vw): 112 px on a folded phone). */
const MIN_PORTRAIT = 100;

/**
 * A card's picture of the cat keeps its size and shape on every screen: a card taller than the screen scrolls, it never
 * squashes the picture (or flattens it to nothing).
 */
async function squashedPortraits(page: Page): Promise<string[]> {
  const boxes = await page.evaluate(() => [...document.querySelectorAll<HTMLElement>("dialog .portrait")]
    .map((portrait) => portrait.getBoundingClientRect())
    .map((r) => ({ width: r.width, height: r.height })));
  return boxes
    .filter(({ width, height }) => width < MIN_PORTRAIT || height < MIN_PORTRAIT || height < width * 0.9)
    .map(({ width, height }) => `the cat's picture is squashed to ${Math.round(width)}x${Math.round(height)}`);
}

/** Every visible button and text box must be at least MIN_TAP square. */
async function smallTapTargets(page: Page): Promise<string[]> {
  const small = await page.evaluate((min) => [...document.querySelectorAll<HTMLElement>("button, input, a[href]")]
    .filter((element) => element.offsetParent !== null)
    .map((element) => ({ text: element.textContent?.trim() || element.getAttribute("placeholder") || element.tagName, r: element.getBoundingClientRect() }))
    .filter(({ r }) => r.width < min || r.height < min)
    .map(({ text, r }) => `"${text}" is ${Math.round(r.width)}x${Math.round(r.height)}`), MIN_TAP);
  return small.map((target) => `tap target too small (under ${MIN_TAP}px): ${target}`);
}

async function smallInputs(page: Page): Promise<string[]> {
  const small = await page.evaluate((min) => [...document.querySelectorAll<HTMLInputElement>("input, textarea")]
    .filter((element) => element.offsetParent !== null && parseFloat(getComputedStyle(element).fontSize) < min)
    .map((element) => `${element.placeholder || element.name || "a text box"} (${getComputedStyle(element).fontSize})`), MIN_INPUT_FONT);
  return small.map((input) => `text box font under ${MIN_INPUT_FONT}px, which zooms the page on iOS: ${input}`);
}

function round(box: { left: number; top: number; right: number; bottom: number }): string {
  return [box.left, box.top, box.right, box.bottom].map(Math.round).join(", ");
}

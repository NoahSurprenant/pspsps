import { mkdir } from "node:fs/promises";
import { join } from "node:path";
import { expect, test, type Browser, type BrowserContext, type Page } from "@playwright/test";
import { pixelsNear, pondBlue, watchErrors } from "./game";

// Every screen at every size: nothing scrolls sideways, every part is on screen and none sits on another, and on phones
// the controls are big enough to tap and the text boxes big enough not to zoom the page when tapped.
//
// Each screen and size is screenshotted to screenshots/<screen>/<n>-<device>-<width>x<height>.png; CI puts each screen's
// sizes side by side (contact-sheets.mjs) and links them on the PR.

/** The screen sizes, by device: the name goes in each screenshot's file name, which the contact sheets order by. */
const SIZES = [
  { device: "desktop", width: 1920, height: 1080, phone: false },
  { device: "laptop", width: 1280, height: 720, phone: false },
  { device: "tablet-landscape", width: 1024, height: 768, phone: false },
  { device: "tablet-portrait", width: 768, height: 1024, phone: false },
  // A phone on its side, with the browser's bars taking some of the height.
  { device: "phone-landscape", width: 844, height: 390, phone: true },
  { device: "phone-portrait", width: 390, height: 844, phone: true },
  { device: "small-phone", width: 360, height: 640, phone: true },
] as const;

type Size = (typeof SIZES)[number];

/** Apple's minimum for a tap target (Google's is 48 dp); WCAG 2.2 AA asks 24 CSS px. Phones are held to the larger. */
const MIN_TAP = 44;
/** iOS Safari zooms the page into a text box whose font is smaller than this. */
const MIN_INPUT_FONT = 16;

interface Screen {
  name: string;
  /** Opens the screen in a fresh browser context at the size; returns once it is ready. */
  open(page: Page): Promise<void>;
  /** The parts that must each be wholly on screen and not on top of one another. */
  parts: string[];
  /** Anything else the screen must show at every size: what is missing. */
  shows?(page: Page): Promise<string[]>;
}

const SCREENS: Screen[] = [
  {
    name: "sign-in",
    open: async (page) => {
      await page.goto("/");
      await expect(page.getByRole("button", { name: "Play as a guest" })).toBeVisible();
    },
    parts: [".panel"],
  },
  {
    name: "worlds",
    open: async (page) => {
      await page.goto("/");
      await page.getByRole("button", { name: "Play as a guest" }).click();
      await expect(page.getByRole("button", { name: /^Tabby · / })).toBeVisible();
    },
    parts: [".panel"],
  },
  {
    name: "town-square",
    open: async (page) => {
      await page.goto("/");
      await page.getByRole("button", { name: "Play as a guest" }).click();
      await page.getByRole("button", { name: /^Calico · / }).click();
      await expect(page.locator(".room-bar strong")).toHaveText("Town Square");
      // The scene has drawn a frame or two.
      await page.waitForTimeout(500);
    },
    parts: [".room-bar", ".emote-bar", ".chat-bar", ".fishing"],
    // The pond, drawn: from further off (a narrow screen backs the camera away) the floor's outline once hid it.
    shows: async (page) =>
      pixelsNear(await page.locator("#game").screenshot(), pondBlue) > 20 ? [] : ["the pond is not drawn"],
  },
];

for (const screen of SCREENS) {
  for (const [index, size] of SIZES.entries()) {
    test(`${screen.name} at ${size.device} ${size.width}x${size.height}`, async ({ browser }) => {
      const context = await contextFor(browser, size);
      try {
        const page = await context.newPage();
        const errors = watchErrors(page);
        await screen.open(page);

        const dir = join("screenshots", screen.name);
        await mkdir(dir, { recursive: true });
        await page.screenshot({ path: join(dir, `${index + 1}-${size.device}-${size.width}x${size.height}.png`) });

        const problems = [
          ...await sidewaysScroll(page),
          ...await offScreenOrOverlapping(page, screen.parts),
          ...(size.phone ? await smallTapTargets(page) : []),
          ...(size.phone ? await smallInputs(page) : []),
          ...(screen.shows ? await screen.shows(page) : []),
        ];
        expect(problems, `${screen.name} at ${size.width}x${size.height}`).toEqual([]);
        expect(errors, "console errors and uncaught exceptions").toEqual([]);
      } finally {
        await context.close();
      }
    });
  }
}

/** A phone gets a phone's browser: touch, the mobile viewport and a high-density screen. */
function contextFor(browser: Browser, size: Size): Promise<BrowserContext> {
  return browser.newContext({
    viewport: { width: size.width, height: size.height },
    ...(size.phone ? { isMobile: true, hasTouch: true, deviceScaleFactor: 2 } : {}),
  });
}

async function sidewaysScroll(page: Page): Promise<string[]> {
  const { scrollWidth, innerWidth } = await page.evaluate(() => ({
    scrollWidth: document.documentElement.scrollWidth,
    innerWidth: window.innerWidth,
  }));
  return scrollWidth > innerWidth ? [`the page scrolls sideways: ${scrollWidth}px wide in ${innerWidth}px`] : [];
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
    if (box.left < 0 || box.top < 0 || box.right > width + 0.5 || box.bottom > height + 0.5) {
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

import type { Browser, BrowserContext, Page } from "@playwright/test";
import { enterWorld, expect } from "./game";

// The game's screens and the browsers they are seen in, shared by the layout and accessibility specs.

export interface Size {
  device: string;
  width: number;
  height: number;
  phone: boolean;
}

export interface Screen {
  name: "sign-in" | "worlds" | "town-square";
  /** Opens the screen in a fresh page; returns once it is ready. */
  open(page: Page): Promise<void>;
}

export const SCREENS: Screen[] = [
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
      await enterWorld(page, "Calico");
    },
  },
];

/**
 * A phone gets a phone's browser: touch, the mobile viewport and a high-density screen. Every size gets the sign-in
 * providers production has, which CI's server doesn't: their buttons make the sign-in and world panels their tallest.
 */
export async function contextFor(browser: Browser, size: Size): Promise<BrowserContext> {
  const context = await browser.newContext({
    viewport: { width: size.width, height: size.height },
    ...(size.phone ? { isMobile: true, hasTouch: true, deviceScaleFactor: 2 } : {}),
  });
  await context.route("**/auth/providers", (route) => route.fulfill({ json: ["Discord", "Google"] }));
  return context;
}

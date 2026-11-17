import type { Browser, BrowserContext, Page } from "@playwright/test";
import { clickCat, enterWorld, expect, goThroughDoor, hubGuest } from "./game";

// The game's screens and the browsers they are seen in, shared by the layout and accessibility specs.

export interface Size {
  device: string;
  width: number;
  height: number;
  phone: boolean;
}

export interface Screen {
  name: "sign-in" | "worlds" | "town-square" | "cafe" | "player-card" | "friends" | "shop";
  /** Opens the screen in a fresh page; returns once it is ready, with what to close after (other players), if any. */
  open(page: Page): Promise<(() => Promise<void>) | void>;
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
  {
    // The Cat Café, reached as a player gets there: through the door from the square.
    name: "cafe",
    open: async (page) => {
      await enterWorld(page, "Calico");
      await goThroughDoor(page, "cafe", "Cat Café");
    },
  },
  {
    // Another cat's card, opened by tapping it: Felix, standing off to one side of the square.
    name: "player-card",
    open: async (page) => {
      const felix = await hubGuest(origin(page));
      try {
        await felix.hub.invoke("EnterWorld", "calico");
        await felix.hub.invoke("Move", 6, 1);
        await enterWorld(page, "Calico");
        await clickCat(page, felix.id);
        await expect(page.getByRole("dialog", { name: felix.name })).toBeVisible();
        await expect(page.getByRole("button", { name: "Add friend" })).toBeVisible();
      } catch (error) {
        await felix.hub.stop();
        throw error;
      }
      return () => felix.hub.stop();
    },
  },
  {
    // The friends list: Felix, a friend in another world's café, and Garfield, asking.
    name: "friends",
    open: async (page) => {
      await enterWorld(page, "Calico");
      const tomId = await page.evaluate(async () => ((await (await fetch("/api/me")).json()) as { id: string }).id);
      const felix = await hubGuest(origin(page));
      const garfield = await hubGuest(origin(page));
      const close = async () => {
        await felix.hub.stop();
        await garfield.hub.stop();
      };
      try {
        await felix.hub.invoke("AddFriend", tomId);
        await felix.hub.invoke("EnterWorld", "tabby");
        await felix.hub.invoke("GoToRoom", "cafe");
        await page.getByRole("button", { name: "Friends", exact: true }).click();
        await page.getByRole("button", { name: `Accept ${felix.name}` }).click();
        await garfield.hub.invoke("AddFriend", tomId);
        await expect(page.getByRole("button", { name: `Go to ${felix.name}` })).toBeEnabled();
        await expect(page.getByRole("button", { name: `Accept ${garfield.name}` })).toBeVisible();
        await expect(page.getByText("Cat Café, Tabby")).toBeVisible();
      } catch (error) {
        await close();
        throw error;
      }
      return close;
    },
  },
  {
    // The shop, for a new guest: nothing bought yet, so every Buy waits for coins and says how many, and the coat it
    // was born with is its fur now.
    name: "shop",
    open: async (page) => {
      await enterWorld(page, "Calico");
      await page.getByRole("button", { name: "Shop", exact: true }).click();
      const shop = page.getByRole("dialog", { name: "Shop" });
      await expect(shop).toBeVisible();
      // It opens at its top, on its heading, not scrolled down to the one button a new guest can press (Close).
      await expect(shop.getByRole("heading", { name: "Shop" })).toBeFocused();
      expect(await shop.evaluate((dialog) => dialog.scrollTop)).toBe(0);
      await expect(shop.getByRole("button", { name: "Buy the Party hat for 30 coins" })).toBeDisabled();
      await expect(shop.getByText("Your fur now")).toBeVisible();
    },
  },
];

/** The game's address, from a page that has opened it. */
function origin(page: Page): string {
  const url = page.url();
  return url.startsWith("http") ? new URL(url).origin : (process.env.E2E_BASE_URL ?? "http://localhost:8080");
}

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

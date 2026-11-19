import type { Browser, BrowserContext, Page } from "@playwright/test";
import {
  banWithNetworks, clickCat, enterWorld, enterWorldSignedIn, expect, fakeNetwork, goHome, goThroughDoor, hubGuest, moderate,
  openCatalogue, otherTab, portraitShown, signInAsAdmin, tapFloor,
} from "./game";

// The game's screens and the browsers they are seen in, shared by the layout and accessibility specs.

export interface Size {
  device: string;
  width: number;
  height: number;
  phone: boolean;
}

export interface Screen {
  name:
    | "sign-in" | "worlds" | "town-square" | "cafe" | "shop-room" | "player-card" | "own-card" | "friends" | "catalogue"
    | "home" | "decorating" | "report" | "admin-card" | "admin-page";
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
    // Another cat's card, opened by tapping it: Felix, standing off to one side of the square, with Mute and Report.
    name: "player-card",
    open: async (page) => {
      const felix = await openFelixsCard(page);
      return () => felix.hub.stop();
    },
  },
  {
    // The report form, from Felix's card: a reason picked and a note written, "Also mute them" ticked.
    name: "report",
    open: async (page) => {
      const felix = await openFelixsCard(page);
      try {
        await page.getByRole("button", { name: `Report ${felix.name}` }).click();
        const form = page.getByRole("dialog", { name: `Report ${felix.name}` });
        await expect(form).toBeVisible();
        await form.getByRole("radio", { name: "Mean or hurtful chat" }).check();
        await form.getByLabel("Anything to add? (optional)").fill("Said mean things about my hat.");
        await expect(form.getByRole("checkbox", { name: "Also mute them" })).toBeChecked();
      } catch (error) {
        await felix.hub.stop();
        throw error;
      }
      return () => felix.hub.stop();
    },
  },
  {
    // The friends list: Felix, a friend in another world's café, Garfield, asking, and Oddie, muted (from another tab:
    // the list shows it as it happens).
    name: "friends",
    open: async (page) => {
      await enterWorld(page, "Calico");
      const tomId = await page.evaluate(async () => ((await (await fetch("/api/me")).json()) as { id: string }).id);
      const felix = await hubGuest(origin(page));
      const garfield = await hubGuest(origin(page));
      const oddie = await hubGuest(origin(page));
      const tab = await otherTab(page, origin(page));
      const close = async () => {
        await felix.hub.stop();
        await garfield.hub.stop();
        await oddie.hub.stop();
        await tab.stop();
      };
      try {
        await felix.hub.invoke("AddFriend", tomId);
        await felix.hub.invoke("EnterWorld", "tabby");
        await felix.hub.invoke("GoToRoom", "cafe");
        await page.getByRole("button", { name: "Friends", exact: true }).click();
        await page.getByRole("button", { name: `Accept ${felix.name}` }).click();
        await garfield.hub.invoke("AddFriend", tomId);
        await tab.invoke("Mute", oddie.id);
        await expect(page.getByRole("button", { name: `Go to ${felix.name}` })).toBeEnabled();
        await expect(page.getByRole("button", { name: `Accept ${garfield.name}` })).toBeVisible();
        await expect(page.getByRole("button", { name: `Unmute ${oddie.name}` })).toBeVisible();
        await expect(page.getByText("Cat Café, Tabby")).toBeVisible();
      } catch (error) {
        await close();
        throw error;
      }
      return close;
    },
  },
  {
    // The Cat Shop, reached as a player gets there: through its door, one of the square's shopfronts.
    name: "shop-room",
    open: async (page) => {
      await enterWorld(page, "Calico");
      await goThroughDoor(page, "shop", "Cat Shop");
    },
  },
  {
    // The catalogue, opened at its stand in the shop, for a new guest: nothing bought yet, so every Buy waits for coins
    // and says how many, and the coat it was born with is its fur now.
    name: "catalogue",
    open: async (page) => {
      await enterWorld(page, "Calico");
      await goThroughDoor(page, "shop", "Cat Shop");
      await openCatalogue(page);
      const catalogue = page.getByRole("dialog", { name: "Catalogue" });
      // It opens at its top, on its heading, not scrolled down to the one button a new guest can press (Close).
      await expect(catalogue.getByRole("heading", { name: "Catalogue" })).toBeFocused();
      expect(await catalogue.evaluate((dialog) => dialog.scrollTop)).toBe(0);
      await expect(catalogue.getByRole("button", { name: "Buy the Party hat for 30 coins" })).toBeDisabled();
      await expect(catalogue.getByText("Your fur now")).toBeVisible();
    },
  },
  {
    // The player's own card, opened by tapping their own cat: its picture, its fur, and (for a new guest) nothing more to
    // wear yet.
    name: "own-card",
    open: async (page) => {
      const me = await enterWorld(page, "Calico");
      const name = await page.evaluate(async () => ((await (await fetch("/api/me")).json()) as { displayName: string }).displayName);
      await clickCat(page, me);
      const card = page.getByRole("dialog", { name });
      await expect(card).toBeVisible();
      await expect(card.getByText("Your fur now")).toBeVisible();
      await portraitShown(page);
    },
  },
  {
    // A new guest's home, gone to from the friends list: the bed it was born with not put out yet.
    name: "home",
    open: async (page) => {
      await enterWorld(page, "Calico");
      await goHome(page);
      await expect(page.getByRole("button", { name: "Decorate" })).toBeVisible();
      await expect(page.getByLabel("Who can visit")).toHaveValue("Friends");
    },
  },
  {
    // Decorating that home: its bed put out with a tap on the floor, and selected, to turn it or put it away.
    name: "decorating",
    open: async (page) => {
      await enterWorld(page, "Calico");
      await goHome(page);
      await page.getByRole("button", { name: "Decorate" }).click();
      await page.getByRole("button", { name: "Cat bed · 1 left" }).click();
      await tapFloor(page, 2.5, 2.5);
      await expect(page.getByRole("button", { name: "Turn the Cat bed" })).toBeVisible();
      await expect(page.getByRole("button", { name: "Cat bed · 0 left" })).toBeDisabled();
      // Its heading and Done at the top of the panel, in view however short the screen: not scrolled off.
      await expect(page.getByRole("heading", { name: "Decorating" })).toBeInViewport({ ratio: 1 });
      await expect(page.getByRole("button", { name: "Done decorating" })).toBeInViewport({ ratio: 1 });
    },
  },
  {
    // Another cat's card as an admin sees it: Felix, muted an hour ago, with Moderate opened (the reason, Warn, Mute,
    // Ban, the network Felix played from this week to ban with him, and Lift).
    name: "admin-card",
    open: async (page) => {
      await signInAsAdmin(page.context(), origin(page));
      const felix = await hubGuest(origin(page), fakeNetwork());
      try {
        await felix.hub.invoke("EnterWorld", "calico");
        await felix.hub.invoke("Move", 6, 1);
        await enterWorldSignedIn(page, "Calico");
        await moderate(page, felix.id, { action: "mute", reason: "Rude in chat", length: "1h" });
        await clickCat(page, felix.id);
        const card = page.getByRole("dialog", { name: felix.name });
        await card.getByText("Moderate", { exact: true }).click();
        await expect(card.getByRole("button", { name: `Lift ${felix.name}` })).toBeVisible();
        await expect(card.getByText(/^Network [0-9a-f]{8} · 1 cat this week$/)).toBeVisible();
        await portraitShown(page);
      } catch (error) {
        await felix.hub.stop();
        throw error;
      }
      return () => felix.hub.stop();
    },
  },
  {
    // The admin page: a report waiting, with its chat; who is banned or muted (a cat banned for a day, at least); the
    // networks banned (Felix's, at least); and what admins did lately.
    name: "admin-page",
    open: async (page) => {
      await signInAsAdmin(page.context(), origin(page));
      const felix = await hubGuest(origin(page), fakeNetwork());
      const tom = await hubGuest(origin(page));
      try {
        await felix.hub.invoke("EnterWorld", "siamese");
        await tom.hub.invoke("EnterWorld", "siamese");
        await felix.hub.invoke("Say", "nobody likes you, and that is a long line to show how a report wraps");
        await tom.hub.invoke("Report", felix.id, "MeanChat", "said it to me", false);
      } finally {
        await felix.hub.stop();
        await tom.hub.stop();
      }
      // Banned with his network (from the admin's own hub connection).
      const [network] = await banWithNetworks(origin(page), felix.id, "Spamming the square");
      await page.goto("/admin");
      await expect(page.getByRole("heading", { name: "Moderation" })).toBeVisible();
      await expect(page.getByRole("heading", { name: `${tom.name} reported ${felix.name}` }).first()).toBeVisible();
      await expect(page.getByRole("button", { name: `Lift ${felix.name}'s ban` }).first()).toBeVisible();
      await expect(page.getByRole("button", { name: `Lift the ban on ${felix.name}'s network ${network}` })).toBeVisible();
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

/** Opens Felix's card, Felix a new guest standing off to one side of Calico's square; returns Felix (stop his hub after). */
async function openFelixsCard(page: Page): Promise<Awaited<ReturnType<typeof hubGuest>>> {
  const felix = await hubGuest(origin(page));
  try {
    await felix.hub.invoke("EnterWorld", "calico");
    await felix.hub.invoke("Move", 6, 1);
    await enterWorld(page, "Calico");
    await clickCat(page, felix.id);
    await expect(page.getByRole("dialog", { name: felix.name })).toBeVisible();
    await expect(page.getByRole("button", { name: "Add friend" })).toBeVisible();
    await expect(page.getByRole("button", { name: `Mute ${felix.name}` })).toBeVisible();
    await expect(page.getByRole("button", { name: `Report ${felix.name}` })).toBeVisible();
    // With its picture, taken.
    await portraitShown(page);
  } catch (error) {
    await felix.hub.stop();
    throw error;
  }
  return felix;
}

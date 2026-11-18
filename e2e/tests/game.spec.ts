import { PNG } from "pngjs";
import {
  animation, catEyes, cats, clickCat, emotes, enterWorld, expect, fishFor, goHome, goThroughDoor, hubGuest, newPlayer,
  pixelsNear, pondBlue, roomBarButtons, tapFloor, test, watchErrors, expectRoom, openCatalogue, partyHatPink, portrait, focusByTab,
  enterWorldSignedIn, signInAsAdmin,
} from "./game";

test("the sign-in page loads without errors", async ({ page }) => {
  await page.goto("/");

  await expect(page.getByRole("heading", { name: "pspsps" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Play as a guest" })).toBeVisible();
});

test("a guest picks a world and the room draws", async ({ page }) => {
  await page.goto("/");
  await page.getByRole("button", { name: "Play as a guest" }).click();
  await expect(page.getByRole("heading", { name: /^Hi, Kitty \d{4}!$/ })).toBeVisible();
  for (const world of ["Tabby", "Calico", "Siamese"]) {
    await expect(page.getByRole("button", { name: new RegExp(`^${world} · \\d+/300$`) })).toBeVisible();
  }

  await page.getByRole("button", { name: /^Tabby · / }).click();

  await expectRoom(page, "Town Square");
  // Before a room shows, the canvas is one flat sky; a drawn room has floor, wall, outlines and a cat. The HTML over
  // the canvas is hidden for the capture, so its text can't pass for a room.
  await page.locator("#ui").evaluate((ui) => (ui.style.visibility = "hidden"));
  await expect.poll(async () => colours(await page.locator("#game").screenshot()), { timeout: 15_000 })
    .toBeGreaterThan(20);
  // Town Square's pond is water-blue (#5fb3d9), seen from above: a disc facing away from the camera would show only
  // its dark outline.
  await expect.poll(async () => pixelsNear(await page.locator("#game").screenshot(), pondBlue), { timeout: 15_000 })
    .toBeGreaterThan(50);
});

// The first room waits for the 3D scene to download: on a slow phone a player may tap another world meanwhile.
test("a world tapped while another is loading is ignored", async ({ page }) => {
  await page.goto("/");
  await page.getByRole("button", { name: "Play as a guest" }).click();
  await expect(page.getByRole("button", { name: /^Calico · / })).toBeVisible();
  // A slow network: the scene's scripts take a while.
  await page.route("**/assets/**", async (route) => {
    await new Promise((resolve) => setTimeout(resolve, 1500));
    await route.continue();
  });

  await page.getByRole("button", { name: /^Tabby · / }).click();
  await page.getByRole("button", { name: /^Calico · / }).click();

  await expectRoom(page, "Town Square");
  await page.waitForTimeout(2000);
  expect(await page.evaluate(() => window.pspsps?.room()?.snapshot.worldId)).toBe("tabby");
});

test("two cats see each other walk, chat and leave", async ({ page: tom, browser }) => {
  const felix = await newPlayer(browser);
  try {
    const tomId = await enterWorld(tom, "Calico");
    const felixId = await enterWorld(felix.page, "Calico");

    // Both are in the room, on both screens.
    await expect.poll(async () => Object.keys(await cats(tom)).sort()).toEqual([tomId, felixId].sort());
    await expect.poll(async () => Object.keys(await cats(felix.page)).sort()).toEqual([tomId, felixId].sort());

    // Felix clicks the middle of the floor: Tom sees Felix leave the spawn point (0, 4).
    const spawn = (await cats(tom))[felixId];
    expect(spawn).toMatchObject({ x: 0, z: 4 });
    const canvas = await felix.page.locator("#game").boundingBox();
    if (!canvas) throw new Error("no canvas");
    await felix.page.mouse.click(canvas.x + canvas.width / 2, canvas.y + canvas.height / 2);
    await expect.poll(async () => (await cats(tom))[felixId]?.z).toBeLessThan(3);

    // Felix chats: Tom gets the line.
    await felix.page.getByPlaceholder("Say something…").fill("pspsps");
    await felix.page.getByPlaceholder("Say something…").press("Enter");
    await expect.poll(() => tom.evaluate(() => window.pspsps?.room()?.chat.map((line) => line.text)))
      .toContain("pspsps");

    // Felix goes through the door to the café: he leaves Tom's room.
    await goThroughDoor(felix.page, "cafe", "Cat Café");
    await expect.poll(async () => Object.keys(await cats(tom))).toEqual([tomId]);

    expect(felix.errors, "Felix's console errors and uncaught exceptions").toEqual([]);
  } finally {
    await felix.close();
  }
});

test("filtered words never reach another cat", async ({ page: tom, browser }) => {
  // The word lists CI starts the game with (.github/workflows/ci.yml): "heck" is masked, "woof" refused.
  const felix = await newPlayer(browser);
  try {
    await enterWorld(tom, "Siamese");
    const felixId = await enterWorld(felix.page, "Siamese");
    await expect.poll(async () => Object.keys(await cats(tom))).toContain(felixId);
    const felixSaid = () => tom.evaluate(
      (id) => window.pspsps?.room()?.chat.filter((line) => line.playerId === id).map((line) => line.text),
      felixId,
    );
    const chat = felix.page.getByPlaceholder("Say something…");

    // A masked word arrives starred out, however Felix spells it.
    await chat.fill("what the h3ck");
    await chat.press("Enter");
    await expect.poll(felixSaid).toEqual(["what the ****"]);

    // A refused line isn't said at all: Felix is told why, and the next line Tom gets from Felix is the one after it.
    await chat.fill("w o o f");
    await chat.press("Enter");
    await expect(felix.page.getByText("Keep it friendly: that message has a word this game doesn't allow."))
      .toBeVisible();
    await chat.fill("pspsps");
    await chat.press("Enter");
    await expect.poll(felixSaid).toEqual(["what the ****", "pspsps"]);

    expect(felix.errors, "Felix's console errors and uncaught exceptions").toEqual([]);
  } finally {
    await felix.close();
  }
});

test("a cat's emotes show on the other cat's screen", async ({ page: tom, browser }) => {
  const felix = await newPlayer(browser);
  try {
    const tomId = await enterWorld(tom, "Siamese");
    const felixId = await enterWorld(felix.page, "Siamese");
    await expect.poll(async () => Object.keys(await cats(tom))).toContain(felixId);

    // From the emote bar.
    await felix.page.getByRole("button", { name: "Wave" }).click();
    await expect.poll(async () => (await emotes(tom))[felixId]).toBe("wave");

    // A number typed in the chat box is just typing. Past the one-second wait after the wave, so a "4" that did reach
    // the server as an emote would be let through and Tom would see Felix fall asleep.
    await felix.page.waitForTimeout(1_500);
    const chat = felix.page.getByPlaceholder("Say something…");
    await chat.fill("");
    await chat.press("4");
    await expect(chat).toHaveValue("4");
    await felix.page.waitForTimeout(1_500);
    expect((await emotes(tom))[felixId]).toBe("wave");

    // The same key anywhere else is the shortcut: 1 sits.
    await felix.page.evaluate(() => (document.activeElement as HTMLElement | null)?.blur());
    await felix.page.keyboard.press("1");
    await expect.poll(async () => (await emotes(tom))[felixId]).toBe("sit");
    expect((await cats(tom))[felixId]?.pose).toBe("sit");
    expect(Object.keys(await emotes(felix.page))).toContain(felixId);
    expect((await emotes(tom))[tomId]).toBeUndefined();

    expect(felix.errors, "Felix's console errors and uncaught exceptions").toEqual([]);
  } finally {
    await felix.close();
  }
});

test("a cat that arrives later sees a sleeping cat asleep", async ({ page: tom, browser }) => {
  const felix = await newPlayer(browser);
  try {
    const felixId = await enterWorld(felix.page, "Siamese");
    await felix.page.getByRole("button", { name: "Sleep" }).click();
    await expect.poll(async () => (await cats(felix.page))[felixId]?.pose).toBe("sleep");

    await enterWorld(tom, "Siamese");

    expect((await cats(tom))[felixId]?.pose).toBe("sleep");
    await expect.poll(() => animation(tom, felixId)).toBe("sleep");
    expect(felix.errors, "Felix's console errors and uncaught exceptions").toEqual([]);
  } finally {
    await felix.close();
  }
});

test("a cat is drawn, walks with its walk animation and idles when it stops", async ({ page }) => {
  const playerId = await enterWorld(page, "Calico");

  // The model loads on the way into the room: the cat idles once it's drawn, facing the camera with its yellow eyes.
  await expect.poll(() => animation(page, playerId)).toBe("idle");
  await page.locator("#ui").evaluate((ui) => (ui.style.visibility = "hidden"));
  await expect.poll(async () => pixelsNear(await page.locator("#game").screenshot(), catEyes, 1)).toBeGreaterThan(10);
  await page.locator("#ui").evaluate((ui) => (ui.style.visibility = ""));

  // Sent to the middle of the room, 4 m from the spawn point: it walks there (a second), then idles.
  const canvas = await page.locator("#game").boundingBox();
  if (!canvas) throw new Error("no canvas");
  await page.mouse.click(canvas.x + canvas.width / 2, canvas.y + canvas.height / 2);
  await expect.poll(() => animation(page, playerId), { intervals: [50] }).toBe("walk");
  await expect.poll(() => animation(page, playerId)).toBe("idle");

  // The poses and the wave are animations too.
  await page.getByRole("button", { name: "Sit" }).click();
  await expect.poll(() => animation(page, playerId)).toBe("sit");
  // The server takes one emote a second.
  await page.waitForTimeout(1_100);
  await page.getByRole("button", { name: "Wave" }).click();
  await expect.poll(() => animation(page, playerId), { intervals: [50] }).toBe("wave");
  await expect.poll(() => animation(page, playerId)).toBe("sit");
});

/** How many distinct colours a PNG has (sampled), to tell a drawn room from a flat background. */
function colours(png: Buffer): number {
  const image = PNG.sync.read(png);
  const seen = new Set<number>();
  for (let i = 0; i < image.data.length; i += 4 * 97) {
    seen.add((image.data[i]! << 16) | (image.data[i + 1]! << 8) | image.data[i + 2]!);
  }
  return seen.size;
}

test("a second tab takes over and the first says so", async ({ page: first, context }) => {
  const playerId = await enterWorld(first, "Tabby");
  const second = await context.newPage();
  const secondErrors = watchErrors(second);

  // Same browser, same cookie: the same player. No sign-in this time; straight to the worlds.
  await second.goto("/");
  await second.getByRole("button", { name: /^Tabby · / }).click();
  await expectRoom(second, "Town Square");

  await expect(first.getByText("You've joined from another tab.")).toBeVisible();
  await expect(first.getByRole("button", { name: "Play here instead" })).toBeVisible();
  // One cat for the player, still there after the first tab closes.
  await expect.poll(async () => Object.keys(await cats(second)).filter((id) => id === playerId)).toHaveLength(1);
  await first.close();
  await second.waitForTimeout(1_000);
  expect(Object.keys(await cats(second))).toContain(playerId);

  expect(secondErrors, "the second tab's console errors and uncaught exceptions").toEqual([]);
});

test("coming back from saving the cat says how it went, once", async ({ page }) => {
  await page.goto("/");
  await page.getByRole("button", { name: "Play as a guest" }).click();
  await expect(page.getByRole("heading", { name: /^Hi, Kitty \d{4}!$/ })).toBeVisible();

  // Where /auth/callback sends a guest whose login already has another cat: still the guest, told why.
  await page.goto("/?link=taken");
  await expect(page.getByRole("heading", { name: /^Hi, Kitty \d{4}!$/ })).toBeVisible();
  await expect(page.getByText("That account already has its own cat")).toBeVisible();
  await expect(page).toHaveURL(/\/$/);

  await page.reload();
  await expect(page.getByRole("heading", { name: /^Hi, Kitty \d{4}!$/ })).toBeVisible();
  await expect(page.getByText("That account already has its own cat")).toBeHidden();
});

test("the panel fits a phone's screen", async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 667 });
  await page.goto("/");
  await page.getByRole("button", { name: "Play as a guest" }).click();
  await expect(page.getByRole("heading", { name: /^Hi, Kitty \d{4}!$/ })).toBeVisible();
  // The longest note: the panel is as wide as it gets.
  await page.goto("/?link=taken");
  await expect(page.getByText("That account already has its own cat")).toBeVisible();

  const panel = await page.locator(".panel").boundingBox();
  if (!panel) throw new Error("no panel");
  expect(panel.x).toBeGreaterThanOrEqual(0);
  expect(panel.x + panel.width).toBeLessThanOrEqual(375);
});

test("a guest fishes at the pond and is paid in coins", async ({ page }) => {
  await enterWorld(page, "Tabby");
  await expect(page.locator("#coins")).toHaveText("🪙 0");

  // Fish walks the cat to the pond's edge and casts.
  await page.getByRole("button", { name: "🎣 Fish" }).click();
  await expect(page.getByText("Waiting for a bite…")).toBeVisible();

  // The bite comes 3-9 s later, at a moment only the server knows; then there are 1.5 s to reel.
  const reel = page.getByRole("button", { name: "Reel!" });
  await expect(reel).toBeVisible({ timeout: 15_000 });
  // A double click reels in once: a second Reel would answer "Cast your line first." over the catch.
  await reel.dblclick();

  await expect(page.locator("#toast")).toHaveText(/^You caught an? .+! \+\d+ coins?$/);
  const coins = await page.evaluate(() => window.pspsps?.coins() ?? 0);
  expect(coins).toBeGreaterThan(0);
  await expect(page.locator("#coins")).toHaveText(`🪙 ${coins}`);
  expect(await page.evaluate(() => window.pspsps?.fishing())).toBe("idle");
});

test("reeling in before the bite loses the fish", async ({ page }) => {
  await enterWorld(page, "Tabby");

  await page.getByRole("button", { name: "🎣 Fish" }).click();
  await expect(page.getByText("Waiting for a bite…")).toBeVisible();
  await page.getByRole("button", { name: "Reel in" }).click();

  await expect(page.locator("#toast")).toHaveText("Too soon! It swam off.");
  expect(await page.evaluate(() => window.pspsps?.coins())).toBe(0);
  await expect(page.getByRole("button", { name: "🎣 Fish" })).toBeVisible();
});

test("Space at a bite reels in, without also pressing the button that has focus", async ({ page }) => {
  const me = await enterWorld(page, "Tabby");

  await page.getByRole("button", { name: "🎣 Fish" }).click();
  await expect(page.getByText("Waiting for a bite…")).toBeVisible();
  // The last button clicked keeps the focus: Space at the bite mustn't wave as well.
  await page.getByRole("button", { name: "Wave" }).focus();
  await expect(page.getByRole("button", { name: "Reel!" })).toBeVisible({ timeout: 15_000 });
  await page.keyboard.press("Space");

  await expect(page.locator("#toast")).toHaveText(/^You caught an? .+! \+\d+ coins?$/);
  // An emote would be back from the server by now.
  await page.waitForTimeout(1_000);
  expect((await emotes(page))[me]).toBeUndefined();
});

test("a bite not reeled in gets away, and the cat can fish again", async ({ page }) => {
  await enterWorld(page, "Tabby");

  await page.getByRole("button", { name: "🎣 Fish" }).click();
  await expect(page.getByRole("button", { name: "Reel!" })).toBeVisible({ timeout: 15_000 });

  await expect(page.locator("#toast")).toHaveText("It got away!", { timeout: 5_000 });
  await expect(page.getByRole("button", { name: "🎣 Fish" })).toBeVisible();
  expect(await page.evaluate(() => window.pspsps?.fishing())).toBe("idle");
});

test("two cats befriend each other from their cards, and one goes to the other", async ({ page: tom, browser }) => {
  const felix = await newPlayer(browser);
  try {
    const tomId = await enterWorld(tom, "Siamese");
    const felixId = await enterWorld(felix.page, "Siamese");
    const tomName = await tom.evaluate(async () => ((await (await fetch("/api/me")).json()) as { displayName: string }).displayName);
    const felixName = await felix.page.evaluate(async () => ((await (await fetch("/api/me")).json()) as { displayName: string }).displayName);
    await expect.poll(async () => Object.keys(await cats(tom))).toContain(felixId);

    // Both cats start on the same spot: Felix walks off, so Tom can tap him.
    const canvas = await felix.page.locator("#game").boundingBox();
    if (!canvas) throw new Error("no canvas");
    await felix.page.mouse.click(canvas.x + canvas.width / 2, canvas.y + canvas.height / 2);
    await expect.poll(async () => (await cats(tom))[felixId]?.z).toBeLessThan(3);

    // Tom taps Felix: his card, with Felix's fur, and "Add friend".
    await clickCat(tom, felixId);
    const card = tom.getByRole("dialog", { name: felixName });
    await expect(card).toBeVisible();
    await expect(card.getByText(/ fur$/)).toBeVisible();
    await card.getByRole("button", { name: "Add friend" }).click();
    await expect(card.getByText("Friend request sent.")).toBeVisible();
    await card.getByRole("button", { name: "Close" }).click();

    // Felix is told, taps Tom, and accepts on Tom's card.
    await expect(felix.page.getByText(`${tomName} wants to be friends!`)).toBeVisible();
    await clickCat(felix.page, tomId);
    const tomsCard = felix.page.getByRole("dialog", { name: tomName });
    await tomsCard.getByRole("button", { name: "Accept" }).click();
    await expect(tomsCard.getByText("You're friends.")).toBeVisible();
    await tomsCard.getByRole("button", { name: "Close" }).click();

    // Felix moves to another world, and its café; Tom's friends list says so, and Tom goes to him.
    await felix.page.getByRole("button", { name: "Worlds" }).click();
    await felix.page.getByRole("button", { name: /^Calico · / }).click();
    await expectRoom(felix.page, "Town Square");
    await goThroughDoor(felix.page, "cafe", "Cat Café");
    await tom.getByRole("button", { name: "Friends", exact: true }).click();
    const list = tom.getByRole("dialog", { name: "Friends" });
    await expect(list.getByText("Cat Café, Calico")).toBeVisible();
    await list.getByRole("button", { name: `Go to ${felixName}` }).click();

    await expectRoom(tom, "Cat Café");
    await expect.poll(async () => Object.keys(await cats(tom)).sort()).toEqual([tomId, felixId].sort());
    expect(await tom.evaluate(() => window.pspsps?.room()?.snapshot.worldId)).toBe("calico");

    expect(felix.errors, "Felix's console errors and uncaught exceptions").toEqual([]);
  } finally {
    await felix.close();
  }
});

test("a Go to that fails leaves the cat playing in its room", async ({ page: tom }) => {
  const tomId = await enterWorld(tom, "Tabby");
  const felix = await hubGuest(new URL(tom.url()).origin);
  const garfield = await hubGuest(new URL(tom.url()).origin);
  try {
    // Felix, Tom's friend, is in Calico; Garfield, also Felix's friend, watches for Felix going offline.
    await felix.hub.invoke("AddFriend", tomId);
    await felix.hub.invoke("AddFriend", garfield.id);
    await garfield.hub.invoke("AcceptFriend", felix.id);
    await felix.hub.invoke("EnterWorld", "calico");
    await tom.getByRole("button", { name: "Friends", exact: true }).click();
    await tom.getByRole("button", { name: `Accept ${felix.name}` }).click();
    const goTo = tom.getByRole("button", { name: `Go to ${felix.name}` });
    await expect(goTo).toBeEnabled();

    // Felix leaves the game while Tom's list still shows him in Calico; Tom taps Go to and is told.
    await felix.hub.stop();
    await expect.poll(async () => {
      const friends = await garfield.hub.invoke<{ playerId: string; place: unknown }[]>("GetFriends");
      return friends.find((f) => f.playerId === felix.id)?.place ?? null;
    }).toBeNull();
    await goTo.click();
    await expect(tom.getByText(`${felix.name} isn't in a room right now.`).first()).toBeVisible();

    // Tom is still in his room, and still plays: he walks.
    await tom.keyboard.press("Escape");
    expect(await tom.evaluate(() => window.pspsps?.room()?.snapshot.worldId)).toBe("tabby");
    const before = (await cats(tom))[tomId];
    const canvas = await tom.locator("#game").boundingBox();
    if (!canvas) throw new Error("no canvas");
    await tom.mouse.click(canvas.x + canvas.width / 2, canvas.y + canvas.height / 2);
    await expect.poll(async () => (await cats(tom))[tomId]?.z).not.toBe(before?.z);
  } finally {
    await felix.hub.stop();
    await garfield.hub.stop();
  }
});

test("the friends list keeps the focus where it was when a request arrives", async ({ page: tom }) => {
  const tomId = await enterWorld(tom, "Siamese");
  const felix = await hubGuest(new URL(tom.url()).origin);
  const garfield = await hubGuest(new URL(tom.url()).origin);
  try {
    await garfield.hub.invoke("AddFriend", tomId);
    await tom.getByRole("button", { name: "Friends", exact: true }).click();
    const decline = tom.getByRole("button", { name: `Decline ${garfield.name}` });
    await decline.focus();

    // Felix asks while Tom is on Garfield's request: the list shows Felix too, and Tom is still where he was.
    await felix.hub.invoke("AddFriend", tomId);
    await expect(tom.getByRole("button", { name: `Accept ${felix.name}` })).toBeVisible();
    await expect(decline).toBeFocused();
  } finally {
    await felix.hub.stop();
    await garfield.hub.stop();
  }
});

test("a guest walks through the café door and comes in by the café's door back", async ({ page }) => {
  const me = await enterWorld(page, "Tabby");
  // Doors are the way around: the room bar has no buttons to other rooms.
  expect(await roomBarButtons(page)).toEqual(["Friends", "Worlds"]);

  // Click the door's mat where it is drawn: the cat walks onto it, then through.
  await goThroughDoor(page, "cafe", "Cat Café");
  // In the café, just inside its door back to the square: by it, not on it, and not at the spawn point.
  const back = await page.evaluate(() => window.pspsps?.room()?.snapshot.doors.find((d) => d.toRoomId === "town-square"));
  if (!back) throw new Error("the café has no door back");
  const cat = (await cats(page))[me];
  if (!cat) throw new Error("the cat isn't in the café");
  const fromDoor = Math.hypot(cat.x - back.x, cat.z - back.z);
  expect(fromDoor).toBeGreaterThan(back.radius);
  expect(fromDoor).toBeLessThan(back.radius + 1);
  expect(cat).not.toMatchObject({ x: 0, z: 3 });
  expect(await roomBarButtons(page)).toEqual(["Friends", "Worlds"]);
});

// The doors are drawn on the canvas, which the keyboard can't reach: each also has a button, shown once it has focus.
test("a keyboard player goes through a door", async ({ page }) => {
  await enterWorld(page, "Tabby");
  await focusByTab(page, page.getByRole("button", { name: "Through the door to Cat Café" }));
  await page.keyboard.press("Enter");

  await expectRoom(page, "Cat Café");
});

test("a cat buys a party hat at the shop's catalogue, wears it from its own card, and another cat sees it", async ({ page: tom, browser, baseURL }) => {
  // The fishing is real (3-9 s a bite), from the hub: about two catches, rarely more than six.
  test.setTimeout(180_000);
  const base = baseURL ?? "http://localhost:8080";
  const fisher = await hubGuest(base);
  let coins: number;
  try {
    coins = await fishFor(fisher.hub, "siamese", 30);
  } finally {
    await fisher.hub.stop();
  }
  const felix = await newPlayer(browser);
  try {
    // The same player now plays in a browser, with the coins they caught.
    const split = fisher.cookie.indexOf("=");
    await tom.context().addCookies([{ name: fisher.cookie.slice(0, split), value: fisher.cookie.slice(split + 1), url: base }]);
    await tom.goto("/");
    await tom.getByRole("button", { name: /^Siamese · / }).click();
    await expectRoom(tom, "Town Square");
    await expect(tom.locator("#coins")).toHaveText(`🪙 ${coins}`);
    // No Shop button: the shop is a place.
    expect(await roomBarButtons(tom)).toEqual(["Friends", "Worlds"]);
    await enterWorld(felix.page, "Siamese");
    await expect.poll(async () => Object.keys(await cats(felix.page))).toContain(fisher.id);

    // Tom walks through the shop's door off the square, and taps the catalogue on its stand: his cat walks up to it, and
    // the catalogue opens.
    await goThroughDoor(tom, "shop", "Cat Shop");
    await openCatalogue(tom);
    const catalogue = tom.getByRole("dialog", { name: "Catalogue" });
    await expect(catalogue.getByText(`You have 🪙 ${coins}`)).toBeVisible();
    await expect(catalogue.getByRole("button", { name: "Buy the Crown for 1000 coins" })).toBeDisabled();
    await catalogue.getByRole("button", { name: "Buy the Party hat for 30 coins" }).click();
    await expect(catalogue.getByText(`You have 🪙 ${coins - 30}`)).toBeVisible();
    // Bought, it's his: worn from his own card, not the catalogue.
    await expect(catalogue.locator(".shop-item", { hasText: "Party hat" }).getByText("Yours")).toBeVisible();
    await expect(catalogue.getByRole("button", { name: "Wear the Party hat" })).toHaveCount(0);
    await catalogue.getByRole("button", { name: "Close" }).click();
    await expect(tom.locator("#coins")).toHaveText(`🪙 ${coins - 30}`);

    // Back in the square, where Felix is, Tom taps his own cat: his card, with a picture of his cat, hatless still.
    await goThroughDoor(tom, "town-square", "Town Square");
    await clickCat(tom, fisher.id);
    const card = tom.getByRole("dialog", { name: fisher.name });
    await expect(card).toBeVisible();
    expect(pixelsNear(await portrait(tom), partyHatPink, 1)).toBeLessThan(5);
    await card.getByRole("button", { name: "Wear the Party hat" }).click();
    await expect(card.getByRole("button", { name: "Take off the Party hat" })).toBeVisible();
    await expect(card.getByText("Wearing: Party hat")).toBeVisible();
    // The picture changes at once: the hat's pink on it.
    await expect.poll(async () => pixelsNear(await portrait(tom), partyHatPink, 1)).toBeGreaterThan(40);
    await expect(card.getByRole("img", { name: new RegExp(`^${fisher.name}: an? \\w+ cat wearing a party hat$`) })).toBeVisible();
    await card.getByRole("button", { name: "Close" }).click();

    // Felix's screen has it on Tom's cat: in the room, and drawn on the model.
    await expect.poll(() => felix.page.evaluate((id) => window.pspsps?.room()?.cats.get(id)?.hat, fisher.id)).toBe("party-hat");
    await expect.poll(() => felix.page.evaluate((id) => window.pspsps?.wearing(id)?.hat, fisher.id)).toBe("party-hat");
    // And Tom's own.
    await expect.poll(() => tom.evaluate((id) => window.pspsps?.wearing(id)?.hat, fisher.id)).toBe("party-hat");

    // Felix taps Tom: his card says what he wears, and its picture shows the hat.
    await clickCat(felix.page, fisher.id);
    const tomsCard = felix.page.getByRole("dialog", { name: fisher.name });
    await expect(tomsCard.getByText("Wearing: Party hat")).toBeVisible();
    await expect.poll(async () => pixelsNear(await portrait(felix.page), partyHatPink, 1)).toBeGreaterThan(40);
    expect(felix.errors, "Felix's console errors and uncaught exceptions").toEqual([]);
  } finally {
    await felix.close();
  }
});

// Buying is the shop's: the server refuses a purchase from anywhere else, whatever a browser asks.
test("buying anywhere but the shop is refused", async ({ baseURL }) => {
  const tom = await hubGuest(baseURL ?? "http://localhost:8080");
  try {
    await tom.hub.invoke("EnterWorld", "tabby");
    await expect(tom.hub.invoke("Buy", "party-hat")).rejects.toThrow("Things are bought from the catalogue in the Cat Shop, off Town Square.");
    // In the shop the place is right: what is refused is the price (a new guest has no coins).
    await tom.hub.invoke("GoToRoom", "shop");
    await expect(tom.hub.invoke("Buy", "party-hat")).rejects.toThrow("The Party hat costs 30 coins. You have 0.");
  } finally {
    await tom.hub.stop();
  }
});

// The cat, the stand and the doors are on the canvas, which the keyboard can't reach: each has a button, shown once it
// has focus.
test("a keyboard player opens its own card, and the catalogue in the shop", async ({ page }) => {
  await enterWorld(page, "Tabby");
  await expect(page.getByRole("button", { name: "Open the catalogue" })).toHaveCount(0);
  await focusByTab(page, page.getByRole("button", { name: "Your cat" }));
  await page.keyboard.press("Enter");
  const name = await page.evaluate(async () => ((await (await fetch("/api/me")).json()) as { displayName: string }).displayName);
  await expect(page.getByRole("dialog", { name })).toBeVisible();
  await page.keyboard.press("Escape");

  await goThroughDoor(page, "shop", "Cat Shop");
  await focusByTab(page, page.getByRole("button", { name: "Open the catalogue" }));
  await page.keyboard.press("Enter");
  await expect(page.getByRole("dialog", { name: "Catalogue" })).toBeVisible({ timeout: 15_000 });
});

test("a cat buys a plant with its catch, puts it out at home, and a friend visits and sees it", async ({ page: tom, browser, baseURL }) => {
  // The fishing is real (3-9 s a bite), from the hub: about three catches.
  test.setTimeout(180_000);
  const base = baseURL ?? "http://localhost:8080";
  const felix = await newPlayer(browser);
  try {
    const felixId = await enterWorld(felix.page, "Tabby");
    const fisher = await hubGuest(base);
    let coins: number;
    try {
      // Tom (fishing from the hub, then playing in a browser) asks Felix to be friends; Felix accepts.
      await fisher.hub.invoke("AddFriend", felixId);
      coins = await fishFor(fisher.hub, "tabby", 35);
    } finally {
      await fisher.hub.stop();
    }
    await felix.page.getByRole("button", { name: "Friends", exact: true }).click();
    await felix.page.getByRole("button", { name: `Accept ${fisher.name}` }).click();
    await felix.page.getByRole("dialog", { name: "Friends" }).getByRole("button", { name: "Close" }).click();

    const split = fisher.cookie.indexOf("=");
    await tom.context().addCookies([{ name: fisher.cookie.slice(0, split), value: fisher.cookie.slice(split + 1), url: base }]);
    await tom.goto("/");
    await tom.getByRole("button", { name: /^Tabby · / }).click();
    await expectRoom(tom, "Town Square");

    // Tom buys a potted plant from the catalogue in the shop, with the coins he caught.
    await goThroughDoor(tom, "shop", "Cat Shop");
    await openCatalogue(tom);
    const shop = tom.getByRole("dialog", { name: "Catalogue" });
    await shop.getByRole("button", { name: "Buy the Potted plant for 35 coins" }).click();
    await expect(shop.getByText(`You have 🪙 ${coins - 35}`)).toBeVisible();
    await expect(shop.getByRole("button", { name: "Buy another Potted plant for 35 coins" })).toBeVisible();
    await shop.getByRole("button", { name: "Close" }).click();

    // He goes home, decorates, and puts the plant out with a tap on the floor.
    await goHome(tom);
    await expectRoom(tom, `${fisher.name}'s home`);
    await tom.getByRole("button", { name: "Decorate" }).click();
    await tom.getByRole("button", { name: "Potted plant · 1 left" }).click();
    await tapFloor(tom, 2.5, 2.5);
    await expect(tom.getByRole("button", { name: "Turn the Potted plant" })).toBeVisible();
    const placed = await tom.evaluate(() => window.pspsps?.home()?.furniture.find((piece) => piece.itemId === "plant"));
    expect(placed?.x).toBeCloseTo(2.5, 1);
    expect(placed?.z).toBeCloseTo(2.5, 1);
    // It turns, then he's done.
    await tom.getByRole("button", { name: "Turn the Potted plant" }).click();
    await expect.poll(() => tom.evaluate(() => window.pspsps?.home()?.furniture.find((p) => p.itemId === "plant"))).toMatchObject({ quarterTurns: 1 });
    await tom.getByRole("button", { name: "Done decorating" }).click();
    await expect(tom.locator(".emote-bar")).toBeVisible();

    // Felix visits Tom's home from his friends list, comes in by its door, and sees the plant where Tom put it.
    await felix.page.getByRole("button", { name: "Friends", exact: true }).click();
    await felix.page.getByRole("button", { name: `Visit home of ${fisher.name}` }).click();
    await expectRoom(felix.page, `${fisher.name}'s home`);
    await expect.poll(() => felix.page.evaluate(() => window.pspsps?.drawnProps().map((p) => p.kind))).toContain("plant");
    await expect.poll(async () => Object.keys(await cats(felix.page))).toContain(fisher.id);
    expect(await felix.page.evaluate(() => window.pspsps?.room()?.snapshot.worldId)).toBe("tabby");
    // A visitor has no Decorate.
    await expect(felix.page.getByRole("button", { name: "Decorate" })).toHaveCount(0);

    // Tom puts it away: Felix sees it go.
    await tom.getByRole("button", { name: "Decorate" }).click();
    await tom.getByRole("button", { name: "Potted plant, 1 of 1" }).click();
    await tom.getByRole("button", { name: "Put away the Potted plant" }).click();
    await expect.poll(() => felix.page.evaluate(() => window.pspsps?.drawnProps().map((p) => p.kind))).not.toContain("plant");

    expect(felix.errors, "Felix's console errors and uncaught exceptions").toEqual([]);
  } finally {
    await felix.close();
  }
});

test("a cat visiting a friend's home goes back to Town Square when they stop being friends", async ({ page: tom }) => {
  const tomId = await enterWorld(tom, "Calico");
  const felix = await hubGuest(new URL(tom.url()).origin);
  try {
    // Felix asks; Tom accepts in his friends list, and visits Felix's home from it.
    await felix.hub.invoke("AddFriend", tomId);
    await tom.getByRole("button", { name: "Friends", exact: true }).click();
    await tom.getByRole("button", { name: `Accept ${felix.name}` }).click();
    await tom.getByRole("button", { name: `Visit home of ${felix.name}` }).click();
    await expectRoom(tom, `${felix.name}'s home`);

    // Felix ends the friendship: his home is for friends, so Tom is sent back to Town Square, in Calico, and told.
    await felix.hub.invoke("RemoveFriend", tomId);
    await expectRoom(tom, "Town Square");
    await expect(tom.getByText(`You and ${felix.name} aren't friends any more`)).toBeVisible();
    expect(await tom.evaluate(() => window.pspsps?.room()?.snapshot.worldId)).toBe("calico");
  } finally {
    await felix.hub.stop();
  }
});

test("an admin mutes and warns a cat from its card, bans another, whose tab is shown why and can't come back, then lifts it", async ({ page: admin, context, browser, baseURL }) => {
  await signInAsAdmin(context, baseURL!);
  const felix = await newPlayer(browser);
  const garfield = await newPlayer(browser);
  try {
    // Three cats in Siamese's Town Square, apart: each arrives on the spawn point, so the first two walk off it.
    await enterWorldSignedIn(admin, "Siamese");
    await tapFloor(admin, -4, 2);
    const felixId = await enterWorld(felix.page, "Siamese");
    await tapFloor(felix.page, 4, 2);
    await expect.poll(async () => (await cats(admin))[felixId]?.x ?? 0).toBeGreaterThan(3.5);
    const garfieldId = await enterWorld(garfield.page, "Siamese");
    const name = (page: typeof admin) =>
      page.evaluate(async () => ((await (await fetch("/api/me")).json()) as { displayName: string }).displayName);
    const [felixName, garfieldName] = [await name(felix.page), await name(garfield.page)];

    // The admin opens Felix's card: Moderate, folded away; open, it says how Felix stands.
    await clickCat(admin, felixId);
    const card = admin.getByRole("dialog", { name: felixName });
    await card.getByText("Moderate", { exact: true }).click();
    await expect(card.getByText("Not banned or muted.")).toBeVisible();

    // Muting needs a reason, which Felix is shown.
    await card.getByRole("button", { name: `Mute ${felixName}` }).click();
    await expect(card.getByText("Say why first")).toBeVisible();
    await card.getByLabel("Why (they're shown this)").fill("Rude in chat");
    await card.getByRole("button", { name: `Mute ${felixName}` }).click();
    await expect(card.getByText(/^Muted until .*: Rude in chat$/)).toBeVisible();

    // Felix can't chat, and sees why and until when.
    await expect(felix.page.locator("#muted-note")).toContainText(/You're muted until .*Why: Rude in chat/);
    await expect(felix.page.getByPlaceholder("Say something…")).toBeHidden();

    // A warning: Felix is shown it once, at once.
    await card.getByLabel("Why (they're shown this)").fill("Please keep it friendly.");
    await card.getByRole("button", { name: `Warn ${felixName}` }).click();
    const notice = felix.page.getByRole("dialog", { name: "A word from the admins" });
    await expect(notice).toContainText("Please keep it friendly.");
    await notice.getByRole("button", { name: "Close" }).click();

    // Garfield is banned: his tab is told why and until when, and stops.
    await card.getByRole("button", { name: "Close" }).click();
    await clickCat(admin, garfieldId);
    const garfieldCard = admin.getByRole("dialog", { name: garfieldName });
    await garfieldCard.getByText("Moderate", { exact: true }).click();
    await garfieldCard.getByLabel("Why (they're shown this)").fill("Spamming the square");
    await garfieldCard.getByLabel(`Ban ${garfieldName} for`).selectOption("24h");
    await garfieldCard.getByRole("button", { name: `Ban ${garfieldName}` }).click();
    await expect(garfield.page.getByText(/^Your cat is banned until /)).toBeVisible();
    await expect(garfield.page.getByText("Why: Spamming the square")).toBeVisible();
    // His cat is gone from the room.
    await expect.poll(async () => Object.keys(await cats(admin))).not.toContain(garfieldId);

    // He can't come back.
    await garfield.page.reload();
    await expect(garfield.page.getByText(/^Your cat is banned until /)).toBeVisible();
    await expect(garfield.page.getByRole("button", { name: /^Siamese · / })).toBeHidden();

    // The admin page lists him, and Lift lets him back.
    await admin.goto("/admin");
    await expect(admin.getByRole("heading", { name: "Moderation" })).toBeVisible();
    const listed = admin.locator("li.player", { hasText: garfieldId });
    await expect(listed).toContainText(/Banned until .*: Spamming the square/);
    await expect(admin.locator("li.player", { hasText: felixId })).toContainText(/Muted until .*: Rude in chat/);
    await listed.getByRole("button", { name: `Lift ${garfieldName}'s ban` }).click();
    await expect(admin.getByRole("status")).toHaveText(`${garfieldName} can play and chat again.`);
    await expect(listed).toBeHidden();
    await expect(admin.getByText(new RegExp(`banned ${garfieldName} until .*: Spamming the square`)).first()).toBeVisible();

    await garfield.page.reload();
    await expect(garfield.page.getByRole("button", { name: /^Siamese · / })).toBeVisible();

    expect(felix.errors, "Felix's console errors and uncaught exceptions").toEqual([]);
    expect(garfield.errors, "Garfield's console errors and uncaught exceptions").toEqual([]);
  } finally {
    await felix.close();
    await garfield.close();
  }
});

test("a player finds their own player ID on their card, and /admin tells someone who isn't an admin so", async ({ page }) => {
  const me = await enterWorld(page, "Tabby");
  await clickCat(page, me);
  const card = page.locator("dialog.own-card");
  await card.getByText("Details", { exact: true }).click();
  await expect(card.locator("#player-id")).toHaveText(me);

  // Asked, not opened: a page that answers 403 is a console error in the browser.
  const response = await page.request.get("/admin");
  expect(response.status()).toBe(403);
  expect(await response.text()).toContain("Not an admin");
  expect(await response.text()).toContain(`<code id="player-id">${me}</code>`);
});

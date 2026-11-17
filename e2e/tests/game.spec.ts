import { PNG } from "pngjs";
import { cats, emotes, enterWorld, expect, newPlayer, pixelsNear, pondBlue, test, watchErrors } from "./game";

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

  await expect(page.locator(".room-bar strong")).toHaveText("Town Square");
  await expect(page.getByRole("button", { name: "Cat Café" })).toBeVisible();
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

  await expect(page.locator(".room-bar strong")).toHaveText("Town Square");
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

    // Felix goes to the café: he leaves Tom's room.
    await felix.page.getByRole("button", { name: "Cat Café" }).click();
    await expect(felix.page.locator(".room-bar strong")).toHaveText("Cat Café");
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
    expect(felix.errors, "Felix's console errors and uncaught exceptions").toEqual([]);
  } finally {
    await felix.close();
  }
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
  await expect(second.locator(".room-bar strong")).toHaveText("Town Square");

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

test("a guest walks through the café door and comes in by the café's door back", async ({ page }) => {
  const me = await enterWorld(page, "Tabby");
  const door = await page.evaluate(() => window.pspsps?.room()?.snapshot.doors.find((d) => d.toRoomId === "cafe"));
  if (!door) throw new Error("Town Square has no door to the café");

  // Click the door's mat where it is drawn: the cat walks onto it, then through.
  const canvas = await page.locator("#game").boundingBox();
  if (!canvas) throw new Error("no canvas");
  const mat = await page.evaluate(({ x, z }) => window.pspsps?.screenPoint(x, z), door);
  if (!mat) throw new Error("the door isn't drawn");
  await page.mouse.click(canvas.x + mat.x, canvas.y + mat.y);

  await expect(page.locator(".room-bar strong")).toHaveText("Cat Café");
  // In the café, just inside its door back to the square: by it, not on it, and not at the spawn point.
  const back = await page.evaluate(() => window.pspsps?.room()?.snapshot.doors.find((d) => d.toRoomId === "town-square"));
  if (!back) throw new Error("the café has no door back");
  const cat = (await cats(page))[me];
  if (!cat) throw new Error("the cat isn't in the café");
  const fromDoor = Math.hypot(cat.x - back.x, cat.z - back.z);
  expect(fromDoor).toBeGreaterThan(back.radius);
  expect(fromDoor).toBeLessThan(back.radius + 1);
  expect(cat).not.toMatchObject({ x: 0, z: 3 });
  // The room bar still goes anywhere, the quick way.
  await expect(page.getByRole("button", { name: "Town Square" })).toBeVisible();
});

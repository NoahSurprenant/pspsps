import { PNG } from "pngjs";
import { cats, enterWorld, expect, newPlayer, test, watchErrors } from "./game";

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
  // The placeholder word lists the image ships (src/Pspsps.Server/chat-filter/chat-filter.json): "heck" is masked,
  // "woof" refused.
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

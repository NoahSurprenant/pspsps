import { expect, signInAsAdmin, test } from "./game";

// The room editor app (/admin/rooms): the list of rooms, then one room on a page of its own.

test("an admin picks a room from the list and sees just that room's layout and versions", async ({ page, context, baseURL }) => {
  await signInAsAdmin(context, baseURL!);

  await page.goto("/admin/rooms");
  await expect(page.getByRole("heading", { name: "Rooms", exact: true })).toBeVisible();
  const rooms = page.getByRole("link", { name: /\(.*\)$/ });
  await expect(rooms.first()).toBeVisible();

  await rooms.first().click();
  await expect(page).toHaveURL(/\/admin\/rooms\/[^/]+$/);
  await expect(page.getByRole("link", { name: "All rooms" })).toBeVisible();
  const layout = page.getByLabel(/layout as JSON$/);
  await expect(layout).not.toHaveValue("");
  const text = await layout.inputValue();
  expect(JSON.parse(text), "the room's layout is JSON").toMatchObject({ width: expect.any(Number), props: expect.any(Array) });

  await page.getByRole("link", { name: "Versions" }).click();
  await expect(page.getByText(/^Version \d+: /).first()).toBeVisible();
});

test("the 3D preview sits beside the picture and follows what is typed, before anything is saved", async ({ page, context, baseURL }) => {
  await signInAsAdmin(context, baseURL!);
  await page.goto("/admin/rooms/shop");
  await expect(page.getByLabel("Top-down view of the room")).toBeVisible();
  const frame = page.frameLocator("iframe[title='3D preview of the room']");
  await expect(frame.locator("canvas")).toBeVisible();

  await page.getByText("Layout as JSON").click();
  const layout = page.getByLabel(/layout as JSON$/);
  const typed = JSON.parse(await layout.inputValue()) as { name: string };
  typed.name = "Typed But Not Saved";
  const asked = page.waitForRequest((request) => request.method() === "POST" && request.url().endsWith("/rooms/shop/preview")
    && (request.postDataJSON() as { layout: { name: string } }).layout.name === "Typed But Not Saved");
  await layout.fill(JSON.stringify(typed));
  const request = await asked;
  const response = await request.response();

  expect(response?.status()).toBe(200);
  expect(((await response!.json()) as { roomName: string }).roomName).toBe("Typed But Not Saved");
  await expect(page.getByText("editing the draft")).toHaveCount(0);
});

test("the look is a form whose changes show in the 3D preview at once", async ({ page, context, baseURL }) => {
  await signInAsAdmin(context, baseURL!);
  await page.goto("/admin/rooms/shop");

  await page.getByRole("link", { name: "Look", exact: true }).click();
  await expect(page.getByLabel("Floor painter", { exact: true })).toBeVisible();
  const asked = page.waitForRequest((request) => request.method() === "POST" && request.url().endsWith("/rooms/shop/preview")
    && (request.postDataJSON() as { look?: { sky?: string } }).look?.sky === "#102030");
  await page.getByLabel("Sky").fill("#102030");
  const response = await (await asked).response();

  expect(response?.status()).toBe(200);
  expect(((await response!.json()) as { draftLook: { sky: string } }).draftLook.sky).toBe("#102030");
});

test("the looks library lists the variants and recipes, and a variant's colours are a form", async ({ page, context, baseURL }) => {
  await signInAsAdmin(context, baseURL!);
  await page.goto("/admin/rooms");

  await page.getByRole("link", { name: "Looks library" }).click();
  await expect(page.getByRole("heading", { name: "Looks library" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Painters" })).toBeVisible();
  await page.getByLabel("New variant name").fill("editor-test-variant");
  await page.getByRole("button", { name: "Add variant" }).click();
  await page.getByLabel("editor-test-variant sky").fill("#102030");
  await page.getByRole("button", { name: "Save editor-test-variant" }).click();
  await expect(page.getByRole("status")).toContainText("editor-test-variant is saved");

  await page.getByRole("group", { name: "editor-test-variant" }).getByRole("button", { name: "Delete" }).click();
  await expect(page.getByRole("status")).toContainText("editor-test-variant is gone");
});

test("a new room is saved as a draft from its own page, and the draft can be discarded", async ({ page, context, baseURL }) => {
  await signInAsAdmin(context, baseURL!);
  await page.goto("/admin/rooms");

  await page.getByLabel("New room ID").fill("editor-test-room");
  await page.getByRole("button", { name: "New room" }).click();
  await expect(page.getByRole("heading", { name: /^editor-test-room/ })).toBeVisible();

  await page.getByRole("button", { name: "Save draft" }).click();
  await expect(page.getByRole("status")).toContainText("Saved");
  await expect(page.getByText("editing the draft")).toBeVisible();

  await page.getByRole("button", { name: "Discard draft" }).click();
  await expect(page).toHaveURL(/\/admin\/rooms$/);
  await expect(page.getByRole("link", { name: /editor-test-room/ })).toHaveCount(0);
});

test("a signed-out visitor does not get the room editor", async ({ request }) => {
  const response = await request.get("/admin/rooms/cafe");

  expect(response.status()).toBe(401);
});

test("an overlay is added with a date window, saved, previewed, and deleted", async ({ page, context, baseURL }) => {
  await signInAsAdmin(context, baseURL!);
  await page.goto("/admin/rooms/shop");

  await page.getByRole("link", { name: "Overlays", exact: true }).click();
  await page.getByRole("button", { name: "New overlay" }).click();
  await page.getByLabel("Overlay name").fill("Editor Test Overlay");
  await page.getByLabel("Overlay starts").fill("2026-12-01");
  await page.getByLabel("Overlay ends (this day is off)").fill("2027-01-01");
  await expect(page.getByText(/every year|2026/).first()).toBeVisible();
  const saved = page.waitForResponse((response) => response.request().method() === "PUT" && response.url().includes("/rooms/shop/overlays/"));
  await page.getByRole("button", { name: "Save overlay" }).click();
  const id = new URL((await saved).url()).pathname.split("/").pop()!;

  expect((await saved).status()).toBe(204);
  const row = page.getByRole("listitem").filter({ hasText: `(${id})` });
  await expect(row).toBeVisible();

  const asked = page.waitForRequest((request) => request.method() === "POST" && request.url().endsWith(`/rooms/shop/preview?overlay=${id}`));
  await row.getByRole("button", { name: "Preview" }).click();
  expect((await (await asked).response())?.status()).toBe(200);

  await page.getByRole("link", { name: "Overlays", exact: true }).click();
  await page.getByRole("listitem").filter({ hasText: `(${id})` }).getByRole("button", { name: "Delete" }).click();
  await expect(page.getByRole("listitem").filter({ hasText: `(${id})` })).toHaveCount(0);
});

test("the rooms list offers the content download and the latest changes", async ({ page, context, baseURL }) => {
  await signInAsAdmin(context, baseURL!);
  await page.goto("/admin/rooms");

  await expect(page.getByRole("link", { name: /Download everything as JSON/ })).toHaveAttribute("href", "/api/admin/export");
  const asked = page.waitForResponse((response) => response.url().includes("/api/admin/rooms/audit"));
  await page.getByRole("button", { name: "Show the latest changes" }).click();
  expect((await asked).status()).toBe(200);
});

test("a draft is scheduled to go live later and taken off the schedule again", async ({ page, context, baseURL }) => {
  await signInAsAdmin(context, baseURL!);
  await page.goto("/admin/rooms/shop");
  await page.getByRole("button", { name: "Save draft" }).click();
  await expect(page.getByText("editing the draft")).toBeVisible();

  await page.getByLabel("Go live at (your time)").fill("2099-01-01T09:00");
  await page.getByRole("button", { name: "Publish at that time" }).click();
  await expect(page.getByRole("button", { name: "Take off the schedule" })).toBeVisible();

  await page.getByRole("button", { name: "Take off the schedule" }).click();
  await expect(page.getByRole("button", { name: "Take off the schedule" })).toHaveCount(0);
  await page.getByRole("button", { name: "Discard draft" }).click();
  await expect(page.getByText("editing the draft")).toHaveCount(0);
});

test("every saved draft appears in the room's history, can be shown, and put back", async ({ page, context, baseURL }) => {
  await signInAsAdmin(context, baseURL!);
  await page.goto("/admin/rooms");
  await page.getByLabel("New room ID").fill("history-test-room");
  await page.getByRole("button", { name: "New room" }).click();
  await page.getByRole("button", { name: "Save draft" }).click();
  await expect(page.getByRole("status")).toContainText("Saved");

  await page.getByRole("link", { name: "Versions" }).click();
  await expect(page.getByText("draft-saved")).toHaveCount(1);
  await page.getByRole("button", { name: "Show it" }).click();
  await expect(page.getByText("The room as it was saved at")).toBeVisible();
  await expect(page.locator("iframe")).toBeVisible();
  await page.getByRole("button", { name: "Put it back in the draft" }).click();
  await expect(page.getByRole("status")).toContainText("is the draft again");

  await page.getByRole("button", { name: "Discard draft" }).click();
  await expect(page).toHaveURL(/\/admin\/rooms$/);
});

test("the world map places rooms side by side and shows two rooms on the same floor as a clash", async ({ page, context, baseURL }) => {
  await signInAsAdmin(context, baseURL!);
  await page.goto("/admin/rooms/map");
  await expect(page.getByRole("heading", { name: "World map" })).toBeVisible();
  const rooms = page.locator("ul.rooms button");
  await expect(rooms.nth(1)).toBeVisible();

  for (const index of [0, 1]) {
    await rooms.nth(index).click();
    await page.getByRole("button", { name: "Put on the map" }).click();
  }
  await expect(page.locator(".world-room")).toHaveCount(2);
  await expect(page.getByRole("alert")).toHaveCount(0);

  await page.getByLabel(/ cell east$/).fill("0");
  await page.getByLabel(/ cell east$/).dispatchEvent("change");
  await page.getByLabel(/ cell north$/).fill("0");
  await page.getByLabel(/ cell north$/).dispatchEvent("change");
  await expect(page.getByRole("alert")).toContainText("share floor");

  await page.getByRole("button", { name: "Undo them" }).click();
  await expect(page.locator(".world-room")).toHaveCount(0);
});

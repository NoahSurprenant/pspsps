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

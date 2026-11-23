import { mkdir } from "node:fs/promises";
import { expect, signInAsAdmin, test } from "./game";

// Every screen of the room editor (/admin/rooms) on one contact sheet (screenshots/admin-editor.png), which CI links on
// the PR, so the layout of the screens is seen and judged, not only their behaviour.

test("every screen of the room editor is pictured on a contact sheet", async ({ page, context, browser, baseURL }) => {
  test.setTimeout(180_000);
  await signInAsAdmin(context, baseURL!);
  await page.setViewportSize({ width: 1280, height: 900 });
  const pictures: { name: string; png: Buffer }[] = [];
  const take = async (name: string): Promise<void> => {
    await page.waitForTimeout(1200);
    pictures.push({ name, png: await page.screenshot({ fullPage: true }) });
  };
  const frame = page.frameLocator("iframe[title='3D preview of the room']").locator("canvas");

  await page.goto("/admin/rooms");
  await expect(page.getByRole("link", { name: /\(.*\)$/ }).first()).toBeVisible();
  await take("Rooms list");

  await page.goto("/admin/rooms/shop");
  await expect(frame).toBeVisible();
  await take("Layout");

  await page.getByRole("link", { name: "Look", exact: true }).click();
  await expect(page.getByLabel("Floor painter", { exact: true })).toBeVisible();
  await take("Look");

  await page.getByRole("link", { name: "Overlays", exact: true }).click();
  await take("Overlays");

  await page.getByRole("link", { name: "Versions", exact: true }).click();
  await expect(page.getByText(/^Version \d+: /).first()).toBeVisible();
  await take("Versions");

  await page.goto("/admin/rooms");
  await page.getByRole("link", { name: "Looks library" }).click();
  await take("Looks library");

  await page.goto("/admin/map");
  await expect(page.getByRole("heading", { name: "World map" })).toBeVisible();
  await take("World map");

  await mkdir("screenshots", { recursive: true });
  const sheet = await browser.newPage({ viewport: { width: 2000, height: 900 } });
  try {
    await sheet.setContent(`<!doctype html><html><head><style>
      body { margin: 0; padding: 16px; background: #f4efe4; font: 14px system-ui, sans-serif; color: #2a2230; width: 1968px; }
      h1 { margin: 0 0 12px; font-size: 20px; }
      .row { display: flex; flex-wrap: wrap; gap: 16px; align-items: flex-start; }
      figure { margin: 0; }
      img { width: 960px; display: block; border: 2px solid #2a2230; border-radius: 6px; }
    </style></head><body><h1>Room editor</h1><div class="row">${pictures.map((p) =>
      `<figure><img src="data:image/png;base64,${p.png.toString("base64")}"><figcaption>${p.name}</figcaption></figure>`).join("")
    }</div></body></html>`);
    await sheet.locator("body").screenshot({ path: "screenshots/admin-editor.png" });
  } finally {
    await sheet.close();
  }
});

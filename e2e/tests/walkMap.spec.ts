import { mkdir } from "node:fs/promises";
import { enterWorld, expect, test } from "./game";

// Every place on the world map that is open: a guest travels to it, and it shows a room with a title, doors and no
// console errors (the fixture fails the test on those). Also run against the live game by `npm run test:live`.
test("a guest visits every open place on the world map", async ({ page, browser }, info) => {
  test.setTimeout(240_000);
  await enterWorld(page, "Tabby");
  const openMap = page.getByRole("button", { name: "World map" });
  const map = page.getByRole("dialog", { name: "World map" });

  await openMap.click();
  const places = await map.locator(".map-place[data-place]").evaluateAll((nodes) => nodes.map((node) => ({
    id: node.getAttribute("data-place") ?? "",
    label: node.getAttribute("aria-label") ?? "",
  })));
  await page.keyboard.press("Escape");
  const open = places.filter((place) => !/coming soon|you are here/i.test(place.label));
  expect(open.length, "the map has places to visit").toBeGreaterThan(0);

  const pictures: { name: string; png: Buffer }[] = [];
  for (const place of open) {
    await openMap.click();
    await map.locator(`.map-place[data-place="${place.id}"]`).click();
    await expect.poll(() => page.evaluate(() => window.pspsps?.room()?.snapshot.roomId), `arriving at ${place.id}`).toBe(place.id);
    const snapshot = await page.evaluate(() => window.pspsps?.room()?.snapshot);
    expect(snapshot?.roomName, `${place.id} has a name`).toBeTruthy();
    await expect(page, `${place.id}'s title`).toHaveTitle(`${snapshot?.roomName} · pspsps`);
    expect(snapshot?.doors.length, `${place.id} has a door`).toBeGreaterThan(0);
    await page.waitForTimeout(1500);
    const png = await page.locator("#game").screenshot();
    pictures.push({ name: snapshot?.roomName ?? place.id, png });
    await info.attach(place.id, { body: png, contentType: "image/png" });
  }

  // One picture of every place, for the live-check workflow to keep (outside screenshots/, which CI's own run links).
  await mkdir("live-check", { recursive: true });
  const sheet = await browser.newPage({ viewport: { width: 1400, height: 900 } });
  try {
    await sheet.setContent(`<!doctype html><html><head><style>
      body { margin: 0; padding: 16px; background: #f4efe4; font: 14px system-ui, sans-serif; color: #2a2230; width: max-content; }
      h1 { margin: 0 0 12px; font-size: 20px; }
      .row { display: flex; flex-wrap: wrap; gap: 16px; }
      figure { margin: 0; }
      img { width: 640px; display: block; border: 2px solid #2a2230; border-radius: 6px; }
    </style></head><body><h1>The world map's places</h1><div class="row">${pictures.map((p) =>
      `<figure><img src="data:image/png;base64,${p.png.toString("base64")}"><figcaption>${p.name}</figcaption></figure>`).join("")
    }</div></body></html>`);
    await sheet.locator("body").screenshot({ path: "live-check/world-map.png" });
  } finally {
    await sheet.close();
  }
});

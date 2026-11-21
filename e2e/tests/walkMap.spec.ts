import { enterWorld, expect, test } from "./game";

// Every place on the world map that is open: a guest travels to it, and it shows a room with a title, doors and no
// console errors (the fixture fails the test on those). Also run against the live game by `npm run test:live`.
test("a guest visits every open place on the world map", async ({ page }, info) => {
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

  for (const place of open) {
    await openMap.click();
    await map.locator(`.map-place[data-place="${place.id}"]`).click();
    await expect.poll(() => page.evaluate(() => window.pspsps?.room()?.snapshot.roomId), `arriving at ${place.id}`).toBe(place.id);
    const snapshot = await page.evaluate(() => window.pspsps?.room()?.snapshot);
    expect(snapshot?.roomName, `${place.id} has a name`).toBeTruthy();
    await expect(page, `${place.id}'s title`).toHaveTitle(`${snapshot?.roomName} · pspsps`);
    expect(snapshot?.doors.length, `${place.id} has a door`).toBeGreaterThan(0);
    await page.waitForTimeout(1500);
    await info.attach(place.id, { body: await page.locator("#game").screenshot(), contentType: "image/png" });
  }
});

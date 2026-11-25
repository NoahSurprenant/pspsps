import { readFile, mkdir } from "node:fs/promises";
import { adminCookie, cats, enterWorld, expect, tapFloor, test } from "./game";

// The game with every draft room published, as a player sees it: the drafts in ContentDrafts.json are imported and all
// published, a guest cat arrives in Town Square and walks to the seams between placed rooms, and the game's own page is
// pictured at each (screenshots/published-world.png, which CI links on the PR). It publishes and never rolls back: this
// suite runs on its own runner with its own database, never beside the tests that need the original world.

interface Draft { id: string; layout: { name: string; width: number; depth: number; place?: { x: number; z: number } | null; paths?: { edge: string; offset: number; length: number }[] } }
interface Rect { id: string; name: string; x0: number; z0: number; x1: number; z1: number }

async function admin(baseUrl: string, path: string): Promise<Response> {
  const { name, value } = adminCookie();
  const response = await fetch(new URL(path, baseUrl), { method: "POST", headers: { Cookie: `${name}=${value}`, Origin: baseUrl } });
  expect(response.ok, `POST ${path}: ${response.status}`).toBe(true);
  return response;
}

/** The middle of each edge two placed rooms share and of each path out of a room, nearest the arrival first, one after another. */
function seams(rooms: Rect[], from: { x: number; z: number }, paths: { x: number; z: number; between: string }[]): { x: number; z: number; between: string }[] {
  const found: { x: number; z: number; between: string }[] = [...paths];
  for (const a of rooms) {
    for (const b of rooms) {
      if (a.id >= b.id) continue;
      const overlapZ = [Math.max(a.z0, b.z0), Math.min(a.z1, b.z1)] as const;
      const overlapX = [Math.max(a.x0, b.x0), Math.min(a.x1, b.x1)] as const;
      if ((a.x1 === b.x0 || b.x1 === a.x0) && overlapZ[1] > overlapZ[0]) {
        found.push({ x: a.x1 === b.x0 ? a.x1 : a.x0, z: (overlapZ[0] + overlapZ[1]) / 2, between: `${a.name} and ${b.name}` });
      } else if ((a.z1 === b.z0 || b.z1 === a.z0) && overlapX[1] > overlapX[0]) {
        found.push({ x: (overlapX[0] + overlapX[1]) / 2, z: a.z1 === b.z0 ? a.z1 : a.z0, between: `${a.name} and ${b.name}` });
      }
    }
  }
  const tour: typeof found = [];
  let at = from;
  while (found.length > 0 && tour.length < 20) {
    found.sort((p, q) => Math.hypot(p.x - at.x, p.z - at.z) - Math.hypot(q.x - at.x, q.z - at.z));
    const next = found.shift()!;
    tour.push(next);
    at = next;
  }
  return tour;
}

test("the open world with every draft published looks right where rooms meet", async ({ page, browser, baseURL }) => {
  test.setTimeout(300_000);
  const base = baseURL!;
  const file = JSON.parse(await readFile("../src/Pspsps.Application/Rooms/ContentDrafts.json", "utf8")) as { rooms?: Draft[] };
  const drafts = file.rooms ?? [];
  test.skip(drafts.length === 0, "ContentDrafts.json has no rooms");

  const imported = (await (await admin(base, "/api/admin/import")).json()) as { problems: string[] };
  expect(imported.problems, "the import's problems").toEqual([]);
  const query = drafts.filter((d) => d.id !== "town-square").map((d) => `with=${encodeURIComponent(d.id)}`).join("&");
  await admin(base, `/api/admin/rooms/town-square/publish?${query}`);

  const rooms: Rect[] = drafts.filter((d) => d.layout.place).map((d) => {
    const { x, z } = d.layout.place!;
    return { id: d.id, name: d.layout.name, x0: x * 2, z0: z * 2, x1: x * 2 + d.layout.width, z1: z * 2 + d.layout.depth };
  });

  const middles = drafts.filter((d) => d.layout.place).flatMap((d) => {
    const { x, z } = d.layout.place!;
    const [cx, cz] = [x * 2 + d.layout.width / 2, z * 2 + d.layout.depth / 2];
    return (d.layout.paths ?? []).map((p) => ({
      x: p.edge === "East" ? cx + d.layout.width / 2 + p.length / 2 : p.edge === "West" ? cx - d.layout.width / 2 - p.length / 2 : cx + p.offset,
      z: p.edge === "North" ? cz + d.layout.depth / 2 + p.length / 2 : p.edge === "South" ? cz - d.layout.depth / 2 - p.length / 2 : cz + p.offset,
      between: `a path out of ${d.layout.name}`,
    }));
  });

  const me = await enterWorld(page, "Tabby");
  const where = async (): Promise<{ x: number; z: number }> => {
    const cat = (await cats(page))[me];
    if (!cat) throw new Error("the cat isn't in the room");
    return cat;
  };
  const pictures: { name: string; png: Buffer }[] = [];
  const shoot = async (name: string): Promise<void> => {
    await page.waitForTimeout(1200);
    pictures.push({ name, png: await page.screenshot() });
  };
  const roomNow = (): Promise<string | undefined> => page.evaluate(() => window.pspsps?.room()?.snapshot.roomName);

  await shoot(`Arriving in ${await roomNow()}`);
  const skipped: string[] = [];
  for (const seam of seams(rooms, await where(), middles)) {
    try {
      // Hops of a few metres: the camera follows the cat, so the next spot is always on screen.
      for (let hop = 0; hop < 30; hop++) {
        const cat = await where();
        const left = Math.hypot(seam.x - cat.x, seam.z - cat.z);
        if (left < 2) break;
        const step = Math.min(left, 10);
        const target = { x: cat.x + ((seam.x - cat.x) / left) * step, z: cat.z + ((seam.z - cat.z) / left) * step };
        await tapFloor(page, target.x, target.z);
        await expect.poll(async () => Math.hypot(target.x - (await where()).x, target.z - (await where()).z), { timeout: 10_000 }).toBeLessThan(1.5);
      }
      await shoot(`At the seam of ${seam.between}, in ${await roomNow()}`);
    } catch (error) {
      skipped.push(`${seam.between}: ${error instanceof Error ? error.message.split("\n")[0] : String(error)}`);
    }
  }
  // Every place on the world map, arrived at by the map: each outdoor room as a cat first sees it.
  const openMap = page.getByRole("button", { name: "World map" });
  const map = page.getByRole("dialog", { name: "World map" });
  // A tap on the way may have landed on the map board, which opens the same map: start from a closed one.
  if (await map.isVisible()) await page.keyboard.press("Escape");
  const places = (await openMap.click().then(() => map.locator(".map-place[data-place]").evaluateAll((nodes) =>
    nodes.map((node) => ({ id: (node as HTMLElement).dataset.place!, room: (node as HTMLElement).dataset.room, label: node.getAttribute("aria-label") ?? "" })))));
  await page.keyboard.press("Escape");
  for (const place of places.filter((p) => p.room && !/coming soon/i.test(p.label))) {
    try {
      if (!(await map.isVisible())) await openMap.click();
      await map.locator(`.map-place[data-place="${place.id}"]`).click();
      await page.keyboard.press("Escape");
      await page.waitForFunction((id) => window.pspsps?.room()?.snapshot.roomId === id, place.room, { timeout: 20_000 });
      await shoot(`Arriving by the map in ${await roomNow()}`);
    } catch (error) {
      skipped.push(`map ${place.id}: ${error instanceof Error ? error.message.split("\n")[0] : String(error)}`);
    }
  }
  console.log(`published-world: ${pictures.length} pictures; skipped: ${skipped.join(" | ") || "none"}`);
  test.info().annotations.push(...skipped.map((description) => ({ type: "seam not reached", description })));
  expect(pictures.length, `the arrival and at least two seams were pictured (skipped: ${skipped.join("; ")})`).toBeGreaterThanOrEqual(3);

  await mkdir("screenshots", { recursive: true });
  const sheet = await browser.newPage({ viewport: { width: 2000, height: 900 } });
  try {
    await sheet.setContent(`<!doctype html><html><head><style>
      body { margin: 0; padding: 16px; background: #f4efe4; font: 14px system-ui, sans-serif; color: #2a2230; width: 1964px; }
      h1 { margin: 0 0 12px; font-size: 20px; }
      .row { display: flex; flex-wrap: wrap; gap: 16px; }
      figure { margin: 0; }
      img { width: 960px; display: block; border: 2px solid #2a2230; border-radius: 6px; }
    </style></head><body><h1>The game with every draft published: a guest cat at the seams between rooms</h1><div class="row">${pictures.map((p) =>
      `<figure><img src="data:image/png;base64,${p.png.toString("base64")}"><figcaption>${p.name}</figcaption></figure>`).join("")
    }</div></body></html>`);
    await sheet.locator("body").screenshot({ path: "screenshots/published-world.png" });
  } finally {
    await sheet.close();
  }
});

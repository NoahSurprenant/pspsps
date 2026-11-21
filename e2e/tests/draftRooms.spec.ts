import { mkdir, readFile } from "node:fs/promises";
import { expect, signInAsAdmin, test } from "./game";

// The rooms waiting in ContentDrafts.json, end to end before they are merged: the game this PR built loads the file as
// drafts (no problems), and the admin page's 3D preview draws each draft room with every draft in place, with no
// console errors, a floor and every door leading to a room that exists. The pictures go on one contact sheet
// (screenshots/draft-rooms.png), which CI links on the PR, so the rooms are seen before anything is published.

interface Draft { id: string; layout: { name: string; doors: { id: string; toRoom: string }[] } }

test("every draft room loads, previews without errors, and is pictured on a contact sheet", async ({ page, context, browser, baseURL }) => {
  test.setTimeout(180_000);
  const file = JSON.parse(await readFile("../src/Pspsps.Application/Rooms/ContentDrafts.json", "utf8")) as { rooms?: Draft[] };
  const drafts = file.rooms ?? [];
  test.skip(drafts.length === 0, "ContentDrafts.json has no rooms");

  await signInAsAdmin(context, baseURL!);
  const imported = await page.request.post("/api/admin/import", { headers: { Origin: baseURL! } });
  expect(imported.ok(), "loading the file as drafts").toBe(true);
  const report = (await imported.json()) as { problems: string[] };
  expect(report.problems, "the import's problems").toEqual([]);

  const pictures: { name: string; png: Buffer }[] = [];
  await page.goto("/admin");
  await expect(page.getByRole("heading", { name: "Rooms", exact: true })).toBeVisible();
  for (const draft of drafts) {
    const preview = await page.request.get(`/api/admin/rooms/${draft.id}/preview`);
    expect(preview.ok(), `${draft.id}'s preview`).toBe(true);
    const snapshot = (await preview.json()) as { doors: { id: string; toRoomId: string }[]; rooms: { roomId: string }[] };
    expect(snapshot.doors.length, `${draft.id} has a door`).toBeGreaterThan(0);
    for (const door of snapshot.doors) {
      expect(snapshot.rooms.map((room) => room.roomId), `${draft.id}'s door ${door.id} leads to a room`).toContain(door.toRoomId);
    }

    await page.getByRole("button", { name: new RegExp(`^${draft.layout.name.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")} \\(`) }).click();
    await page.getByRole("button", { name: "Preview in 3D" }).click();
    const canvas = page.getByLabel(`Preview of ${draft.id} in 3D`);
    await expect(canvas).toBeVisible();
    await page.waitForTimeout(1500);
    pictures.push({ name: draft.layout.name, png: await canvas.screenshot() });
  }

  await mkdir("screenshots", { recursive: true });
  const sheet = await browser.newPage({ viewport: { width: 1400, height: 900 } });
  try {
    await sheet.setContent(`<!doctype html><html><head><style>
      body { margin: 0; padding: 16px; background: #f4efe4; font: 14px system-ui, sans-serif; color: #2a2230; width: max-content; }
      h1 { margin: 0 0 12px; font-size: 20px; }
      .row { display: flex; flex-wrap: wrap; gap: 16px; }
      figure { margin: 0; }
      img { width: 640px; display: block; border: 2px solid #2a2230; border-radius: 6px; }
    </style></head><body><h1>Draft rooms</h1><div class="row">${pictures.map((p) =>
      `<figure><img src="data:image/png;base64,${p.png.toString("base64")}"><figcaption>${p.name}</figcaption></figure>`).join("")
    }</div></body></html>`);
    await sheet.locator("body").screenshot({ path: "screenshots/draft-rooms.png" });
  } finally {
    await sheet.close();
  }
});

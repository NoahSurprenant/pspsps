import { mkdir, readFile } from "node:fs/promises";
import { expect, signInAsAdmin, test } from "./game";

// The rooms waiting in ContentDrafts.json, end to end before they are merged: the game this PR built loads the file as
// drafts (no problems), and the room editor's 3D preview draws each draft room with every draft in place, with no
// console errors, a floor and every door leading to a room that exists. The pictures go on one contact sheet, three to a row
// (screenshots/draft-rooms.png), which CI links on the PR, so the rooms are seen before anything is published.

interface Draft { id: string; layout: { name: string; doors: { id: string; toRoom: string }[] } }

test("every draft room loads, previews without errors, and is pictured on a contact sheet", async ({ page, context, browser, baseURL }) => {
  test.setTimeout(360_000);
  const file = JSON.parse(await readFile("../src/Pspsps.Application/Rooms/ContentDrafts.json", "utf8")) as { rooms?: Draft[]; overlays?: { id: string; roomId: string; name: string }[] };
  const drafts = file.rooms ?? [];
  test.skip(drafts.length === 0, "ContentDrafts.json has no rooms");

  await signInAsAdmin(context, baseURL!);
  const imported = await page.request.post("/api/admin/import", { headers: { Origin: baseURL! } });
  expect(imported.ok(), "loading the file as drafts").toBe(true);
  const report = (await imported.json()) as { problems: string[] };
  expect(report.problems, "the import's problems").toEqual([]);

  const pictures: { name: string; png: Buffer }[] = [];
  for (const draft of drafts) {
    const preview = await page.request.get(`/api/admin/rooms/${draft.id}/preview`);
    expect(preview.ok(), `${draft.id}'s preview`).toBe(true);
    const snapshot = (await preview.json()) as { doors: { id: string; toRoomId: string }[]; rooms: { id: string }[] };
    expect(snapshot.doors.length, `${draft.id} has a door`).toBeGreaterThan(0);
    for (const door of snapshot.doors) {
      expect(snapshot.rooms.map((room) => room.id), `${draft.id}'s door ${door.id} leads to a room`).toContain(door.toRoomId);
    }

    await page.goto(`/admin/rooms/${draft.id}`);
    await expect(page.getByText("editing the draft")).toBeVisible();
    const canvas = page.frameLocator("iframe[title='3D preview of the room']").locator("canvas");
    await expect(canvas).toBeVisible();
    await page.waitForTimeout(1500);
    pictures.push({ name: draft.layout.name, png: await canvas.screenshot() });

    // The room's seasonal overlays (the import saves them even while the room is a draft), each pictured
    // with its look on, so a winter rink is judged as well as the pitch it is the rest of the year.
    for (const overlay of (file.overlays ?? []).filter((o) => o.roomId === draft.id)) {
      await page.getByRole("link", { name: "Overlays", exact: true }).click();
      const drawn = page.waitForResponse((response) => response.url().endsWith(`/rooms/${draft.id}/preview?overlay=${overlay.id}`));
      await page.getByRole("listitem").filter({ hasText: `(${overlay.id})` }).getByRole("button", { name: "Preview" }).click();
      expect((await drawn).ok(), `${overlay.id} preview`).toBe(true);
      await expect(canvas).toBeVisible();
      await page.waitForTimeout(1500);
      pictures.push({ name: `${draft.layout.name}: ${overlay.name}`, png: await canvas.screenshot() });
    }
  }

  await mkdir("screenshots", { recursive: true });
  const sheet = await browser.newPage({ viewport: { width: 2000, height: 900 } });
  try {
    await sheet.setContent(`<!doctype html><html><head><style>
      body { margin: 0; padding: 16px; background: #f4efe4; font: 14px system-ui, sans-serif; color: #2a2230; width: 1964px; }
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

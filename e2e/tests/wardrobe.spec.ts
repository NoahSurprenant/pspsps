import { mkdir } from "node:fs/promises";
import { PNG } from "pngjs";
import type { Page } from "@playwright/test";
import { enterWorld, expect, test } from "./game";

// Every hat and collar, worn by cats of several coats, as the player cards picture them: each must show (a picture that
// differs from the same cat wearing nothing), and they are put side by side on a contact sheet
// (screenshots/wardrobe.png), which CI links on the PR, so a change to how they sit is seen up close.

const hats = ["party-hat", "bow", "beanie", "top-hat", "crown"];
const collars = ["red-collar", "blue-collar", "green-collar", "bell-collar"];
// Dark and light coats, plain and patched: a hat's outline must read on black, and its colour on white.
const furs = ["Tuxedo", "Black", "White", "Calico", "Orange"];

interface Outfit {
  fur: string;
  hat: string | null;
  collar: string | null;
}

async function picture(page: Page, outfit: Outfit): Promise<string> {
  const url = await page.evaluate((o) => window.pspsps?.portrait(o), outfit);
  expect(url, `a picture of ${JSON.stringify(outfit)}`).toMatch(/^data:image\/png;base64,/);
  return url ?? "";
}

/** How many of the picture's pixels differ from <other>'s: what the hat or collar adds. */
function differing(url: string, other: string): number {
  const [a, b] = [url, other].map((u) => PNG.sync.read(Buffer.from(u.slice(u.indexOf(",") + 1), "base64")));
  let count = 0;
  for (let i = 0; i < a!.data.length; i += 4) {
    if (Math.abs(a!.data[i]! - b!.data[i]!) + Math.abs(a!.data[i + 1]! - b!.data[i + 1]!) + Math.abs(a!.data[i + 2]! - b!.data[i + 2]!)
      + Math.abs(a!.data[i + 3]! - b!.data[i + 3]!) > 40) {
      count++;
    }
  }
  return count;
}

test("every hat and collar shows on every coat, and the wardrobe's contact sheet is made", async ({ page, browser }) => {
  test.setTimeout(180_000);
  await enterWorld(page, "Siamese");

  const rows: { fur: string; cells: { label: string; url: string }[] }[] = [];
  for (const fur of furs) {
    const bare = await picture(page, { fur, hat: null, collar: null });
    const cells = [{ label: "nothing", url: bare }];
    for (const hat of hats) {
      const url = await picture(page, { fur, hat, collar: null });
      expect(differing(url, bare), `${hat} on ${fur}`).toBeGreaterThan(200);
      cells.push({ label: hat, url });
    }
    for (const collar of collars) {
      const url = await picture(page, { fur, hat: null, collar });
      expect(differing(url, bare), `${collar} on ${fur}`).toBeGreaterThan(200);
      cells.push({ label: collar, url });
    }
    rows.push({ fur, cells });
  }

  // The sheet: a row per coat, its pictures at the size the card draws them twice over.
  await mkdir("screenshots", { recursive: true });
  const sheet = await browser.newPage({ viewport: { width: 1600, height: 900 } });
  try {
    await sheet.setContent(`<!doctype html><html><head><style>
      body { margin: 0; padding: 16px; background: #f4efe4; font: 14px system-ui, sans-serif; color: #2a2230; width: max-content; }
      h1 { margin: 0 0 12px; font-size: 20px; }
      .row { display: flex; gap: 8px; align-items: center; margin-bottom: 8px; }
      .fur { width: 70px; font-weight: 600; }
      figure { margin: 0; text-align: center; }
      img { width: 160px; height: 160px; display: block; background: #fffaf0; border: 1px solid #d8cfbf; border-radius: 6px; }
    </style></head><body><h1>Wardrobe</h1>${rows.map((row) => `<div class="row"><div class="fur">${row.fur}</div>${
      row.cells.map((cell) => `<figure><img src="${cell.url}"><figcaption>${cell.label}</figcaption></figure>`).join("")}</div>`).join("")
    }</body></html>`);
    await sheet.locator("body").screenshot({ path: "screenshots/wardrobe.png" });
  } finally {
    await sheet.close();
  }
});

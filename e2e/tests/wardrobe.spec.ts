import { mkdir } from "node:fs/promises";
import { differing, enterWorld, expect, pictureOf, test } from "./game";

// Every hat and collar, worn by cats of several coats, as the player cards picture them: each must show (a picture that
// differs from the same cat wearing nothing), and they are put side by side on a contact sheet
// (screenshots/wardrobe.png), which CI links on the PR, so a change to how they sit is seen up close.

const hats = ["party-hat", "bow", "beanie", "top-hat", "crown"];
const collars = ["red-collar", "blue-collar", "green-collar", "bell-collar"];
// Dark and light coats, plain and patched: a hat's outline must read on black, and its colour on white.
const furs = ["Tuxedo", "Black", "White", "Calico", "Orange"];

test("every hat and collar shows on every coat, and the wardrobe's contact sheet is made", async ({ page, browser }) => {
  test.setTimeout(180_000);
  await enterWorld(page, "Siamese");

  const rows: { fur: string; cells: { label: string; png: Buffer }[] }[] = [];
  for (const fur of furs) {
    const bare = await pictureOf(page, { fur, hat: null, collar: null });
    const cells = [{ label: "nothing", png: bare }];
    for (const hat of hats) {
      const png = await pictureOf(page, { fur, hat, collar: null });
      expect(differing(png, bare), `${hat} on ${fur}`).toBeGreaterThan(200);
      cells.push({ label: hat, png });
    }
    for (const collar of collars) {
      const png = await pictureOf(page, { fur, hat: null, collar });
      expect(differing(png, bare), `${collar} on ${fur}`).toBeGreaterThan(200);
      cells.push({ label: collar, png });
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
      row.cells.map((cell) => `<figure><img src="data:image/png;base64,${cell.png.toString("base64")}"><figcaption>${cell.label}</figcaption></figure>`).join("")}</div>`).join("")
    }</body></html>`);
    await sheet.locator("body").screenshot({ path: "screenshots/wardrobe.png" });
  } finally {
    await sheet.close();
  }
});

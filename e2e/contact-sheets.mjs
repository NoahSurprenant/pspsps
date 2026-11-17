// Puts the layout test's screenshots side by side, one contact sheet per screen (screenshots/<screen>.png), so a PR's
// look can be reviewed at a glance: computers on the first row, tablets on the second, phones on the third, each
// scaled to the same height with its size underneath. CI uploads each sheet unzipped, which GitHub shows in the browser.
import { chromium } from "@playwright/test";
import { readdir, readFile, stat } from "node:fs/promises";
import { join } from "node:path";

const dir = "screenshots";
const HEIGHT = 320;
const ROWS = [["desktop", "laptop"], ["tablet"], ["phone", "small", "fold"]];

const screens = [];
for (const entry of await readdir(dir)) {
  if ((await stat(join(dir, entry))).isDirectory()) screens.push(entry);
}

const browser = await chromium.launch();
try {
  const page = await browser.newPage({ viewport: { width: 1600, height: 900 } });
  for (const screen of screens.sort()) {
    const files = (await readdir(join(dir, screen))).filter((f) => f.endsWith(".png"))
      .sort((a, b) => Number.parseInt(a) - Number.parseInt(b));
    const shots = await Promise.all(files.map(async (file) => {
      const [, device, size] = file.match(/^\d+-([a-z-]+)-(\d+x\d+)\.png$/) ?? [];
      const data = (await readFile(join(dir, screen, file))).toString("base64");
      return { device, size, data };
    }));
    const rows = ROWS.map((prefixes) => shots.filter((s) => prefixes.some((p) => s.device?.startsWith(p))))
      .filter((row) => row.length > 0);
    const figure = (s) => `<figure><img src="data:image/png;base64,${s.data}"><figcaption>${s.device} · ${s.size.replace("x", "×")}</figcaption></figure>`;
    await page.setContent(`<!doctype html><html><head><style>
      body { margin: 0; padding: 16px; background: #f4efe4; font: 14px system-ui, sans-serif; color: #2a2230; width: max-content; }
      h1 { margin: 0 0 12px; font-size: 20px; }
      .row { display: flex; gap: 16px; align-items: flex-start; margin-bottom: 16px; }
      figure { margin: 0; }
      img { height: ${HEIGHT}px; border: 2px solid #2a2230; border-radius: 6px; display: block; }
      figcaption { margin-top: 4px; }
    </style></head><body><h1>${screen}</h1>${rows.map((row) => `<div class="row">${row.map(figure).join("")}</div>`).join("")}</body></html>`);
    await page.locator("body").screenshot({ path: join(dir, `${screen}.png`) });
    console.log(`screenshots/${screen}.png: ${files.length} sizes`);
  }
} finally {
  await browser.close();
}

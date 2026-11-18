import { expect, test, type Request } from "@playwright/test";
import { enterWorld } from "./game";

// The browser's telemetry, against the CI server, which has it on (ClientTelemetry__Enabled in .github/workflows/ci.yml).
// The browser turns it off under automation (navigator.webdriver, which Playwright sets), so this test, and only this
// one, makes the page see an ordinary browser: an init script in the test's own browser, which no page from the server
// can do. The real server takes the batches (a 204 checks they are ones it accepts) and forwards them to a collector that
// isn't there; the batches are read here, as the page posts them.

interface Batch {
  version: string;
  spans: { name: string; attributes: Record<string, unknown> }[];
  logs: { level: string; message: string; traceId?: string; attributes: Record<string, unknown> }[];
  vitals: { name: string; value: number }[];
}

test("a page sends its errors, a failed load and its hub calls' traces to the server", async ({ page }) => {
  await page.addInitScript(() => Object.defineProperty(Navigator.prototype, "webdriver", { get: () => false }));
  // The cat model fails to load: the room still shows, without its cats drawn.
  await page.route("**/*.glb", (route) => route.fulfill({ status: 500, body: "" }));
  const batches: { batch: Batch; status: Promise<number | undefined> }[] = [];
  page.on("request", (request: Request) => {
    if (request.url().endsWith("/api/client-telemetry") && request.method() === "POST") {
      batches.push({
        batch: request.postDataJSON() as Batch,
        status: request.response().then((response) => response?.status()),
      });
    }
  });
  const meTraceparents: (string | undefined)[] = [];
  page.on("request", (request: Request) => {
    if (new URL(request.url()).pathname === "/api/me") {
      meTraceparents.push(request.headers()["traceparent"]);
    }
  });
  const framesSent: string[] = [];
  page.on("websocket", (socket) => socket.on("framesent", (frame) => framesSent.push(String(frame.payload))));

  const playerId = await enterWorld(page, "Tabby");
  await page.evaluate(() => setTimeout(() => {
    throw new Error("an e2e uncaught error");
  }));

  // A batch goes every five seconds.
  const sent = () => batches.flatMap(({ batch }) => batch.logs.map((line) => line.message));
  await expect.poll(sent, { timeout: 20_000 }).toEqual(expect.arrayContaining([
    "Error: an e2e uncaught error",
    "The cat model didn't load",
    "The hub connection is up",
  ]));

  const all = batches.map(({ batch }) => batch);
  const modelLine = all.flatMap((batch) => batch.logs).find((line) => line.message === "The cat model didn't load");
  expect(modelLine?.level).toBe("error");
  expect(String(modelLine?.attributes["exception.message"])).toContain("500");
  expect(all.every((batch) => /^[0-9A-Za-z._-]+$/.test(batch.version)), "service.version on every batch").toBe(true);
  // Every hub call carries a traceparent in its invocation headers, for the server's hub span to continue.
  const invocations = framesSent.filter((frame) => frame.includes('"type":1'));
  expect(invocations.length).toBeGreaterThan(0);
  for (const invocation of invocations) {
    expect(invocation).toMatch(/"headers":\{"traceparent":"00-[0-9a-f]{32}-[0-9a-f]{16}-0[01]"\}/);
  }
  // The game's own HTTP calls carry a traceparent too (the first /api/me is the game's; the helper's own fetch isn't).
  expect(meTraceparents[0]).toMatch(/^00-[0-9a-f]{32}-[0-9a-f]{16}-0[01]$/);
  // Nothing that names the player.
  expect(JSON.stringify(all)).not.toContain(playerId);
  // And the server took every batch.
  for (const { status } of batches) {
    expect(await status).toBe(204);
  }
});

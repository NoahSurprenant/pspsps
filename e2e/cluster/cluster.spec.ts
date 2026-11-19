import { execFileSync } from "node:child_process";
import { createServer, connect, type Server, type Socket } from "node:net";
import { fileURLToPath } from "node:url";
import { test, expect, type Browser, type Page } from "@playwright/test";
import { adminCookie, cats, enterWorld, expectRoom, hubGuest, moderate, tapFloor, watchErrors } from "../tests/game";

// Two server pods of one game, as plain containers clustered through Redis (pods.sh, started by CI's two-pod job): each
// player's browser talks to one pod, and only the backplane and the grains join them. Runs in that job only
// (playwright.cluster.config.ts).
const podA = process.env.E2E_POD_A ?? "http://localhost:8081";
const podB = process.env.E2E_POD_B ?? "http://localhost:8082";
const pods = fileURLToPath(new URL("./pods.sh", import.meta.url));

/** A player in their own browser context (their own cookie), whose pages go to <baseURL>. */
async function playerOn(browser: Browser, baseURL: string) {
  const context = await browser.newContext({ baseURL });
  const page = await context.newPage();
  return { page, errors: watchErrors(page), close: () => context.close() };
}

/** What the page's room has said, oldest first. */
function chat(page: Page): Promise<string[]> {
  return page.evaluate(() => window.pspsps?.room()?.chat.map((line) => line.text) ?? []);
}

async function say(page: Page, text: string): Promise<void> {
  await page.getByPlaceholder("Say something…").fill(text);
  await page.getByPlaceholder("Say something…").press("Enter");
}

/** Orleans' membership table in Redis: the silos by status (3 active, 6 dead). */
function members(): string[] {
  return execFileSync(pods, ["members"], { encoding: "utf8" }).split("\n").filter(Boolean);
}

/**
 * A TCP proxy on <port> to one pod, switched to another as a Kubernetes Service is when its pods change: connections
 * open stay with their pod until it closes them.
 */
async function proxy(port: number, target: number): Promise<{ to(port: number): void; close(): Promise<void> }> {
  let upstream = target;
  const open = new Set<Socket>();
  const server: Server = createServer((client) => {
    const pod = connect(upstream, "127.0.0.1");
    open.add(client).add(pod);
    client.pipe(pod).pipe(client);
    const end = () => {
      client.destroy();
      pod.destroy();
      open.delete(client);
      open.delete(pod);
    };
    client.on("close", end).on("error", end);
    pod.on("close", end).on("error", end);
  });
  await new Promise<void>((resolve) => server.listen(port, "127.0.0.1", resolve));
  return {
    to: (port) => (upstream = port),
    close: () => {
      open.forEach((socket) => socket.destroy());
      return new Promise((resolve) => server.close(() => resolve()));
    },
  };
}

test("two cats on different pods see each other walk and chat", async ({ browser }) => {
  const tom = await playerOn(browser, podA);
  const felix = await playerOn(browser, podB);
  try {
    expect(members().filter((m) => m.startsWith("3 ")), "two active silos").toHaveLength(2);
    const tomId = await enterWorld(tom.page, "Tabby");
    const felixId = await enterWorld(felix.page, "Tabby");

    // One room, whichever pod holds its grain: both see both.
    await expect.poll(async () => Object.keys(await cats(tom.page)).sort()).toEqual([tomId, felixId].sort());
    await expect.poll(async () => Object.keys(await cats(felix.page)).sort()).toEqual([tomId, felixId].sort());

    // Felix (pod B) walks: Tom (pod A) sees him go; and the other way round.
    await tapFloor(felix.page, 4, 2);
    await expect.poll(async () => (await cats(tom.page))[felixId]?.x ?? 0).toBeGreaterThan(3.5);
    await tapFloor(tom.page, -4, 2);
    await expect.poll(async () => (await cats(felix.page))[tomId]?.x ?? 0).toBeLessThan(-3.5);

    // Each one's chat reaches the other.
    await say(tom.page, "hello from pod a");
    await expect.poll(() => chat(felix.page)).toContain("hello from pod a");
    await say(felix.page, "hello from pod b");
    await expect.poll(() => chat(tom.page)).toContain("hello from pod b");

    expect(tom.errors, "Tom's console errors").toEqual([]);
    expect(felix.errors, "Felix's console errors").toEqual([]);
  } finally {
    await tom.close();
    await felix.close();
  }
});

test("a ban decided on pod A closes the player's connections on pod B, and takes their cat out", async ({ browser }) => {
  const admin = await playerOn(browser, podA);
  const garfield = await playerOn(browser, podB);
  // A tab that ignores being told it's banned (no Banned handler): only the server closing it ends it.
  const stubborn = await hubGuest(podB);
  try {
    await admin.page.context().addCookies([{ ...adminCookie(), url: podA }]);
    await admin.page.goto("/");
    await admin.page.getByRole("button", { name: /^Siamese · / }).click();
    await expectRoom(admin.page, "Town Square");
    const garfieldId = await enterWorld(garfield.page, "Siamese");
    const closed = new Promise<void>((resolve) => stubborn.hub.onclose(() => resolve()));
    await stubborn.hub.invoke("EnterWorld", "siamese");
    await expect.poll(async () => Object.keys(await cats(admin.page))).toEqual(expect.arrayContaining([garfieldId, stubborn.id]));

    await moderate(admin.page, stubborn.id, { action: "ban", reason: "Spamming the square", length: "24h" });
    await moderate(admin.page, garfieldId, { action: "ban", reason: "Spamming the square", length: "24h" });

    // Pod B closes the stubborn tab's connection, though the ban was decided on pod A.
    await expect(closed, "pod B closed the banned player's connection").resolves.toBeUndefined();
    // Garfield's tab (on pod B) is told why, and both cats leave the room the admin sees.
    await expect(garfield.page.getByText(/^Your cat is banned until /)).toBeVisible();
    await expect(garfield.page.getByText("Why: Spamming the square")).toBeVisible();
    await expect.poll(async () => Object.keys(await cats(admin.page))).not.toContain(garfieldId);
    await expect.poll(async () => Object.keys(await cats(admin.page))).not.toContain(stubborn.id);

    // Neither comes back, on either pod.
    const back = await fetch(new URL("/api/me", podB), { headers: { Cookie: stubborn.cookie } });
    expect(((await back.json()) as { ban: unknown }).ban, "pod B knows the ban").not.toBeNull();
  } finally {
    await stubborn.hub.stop();
    await admin.close();
    await garfield.close();
  }
});

test("a rolling update: pod A is replaced by a new pod while the game goes on", async ({ browser }) => {
  // Tom reaches pod A through a proxy, as through a Service; Felix stays on pod B.
  const service = await proxy(8090, 8081);
  const tom = await playerOn(browser, "http://localhost:8090");
  const felix = await playerOn(browser, podB);
  try {
    const tomId = await enterWorld(tom.page, "Calico");
    const felixId = await enterWorld(felix.page, "Calico");
    await tapFloor(felix.page, 4, 2);
    await expect.poll(async () => (await cats(tom.page))[felixId]?.x ?? 0).toBeGreaterThan(3.5);
    await say(felix.page, "before");
    await expect.poll(() => chat(tom.page)).toContain("before");

    // A new pod first (maxSurge 1), then the Service sends new connections to it, then the old pod stops (SIGTERM).
    execFileSync(pods, ["start", "pod-a2", "8083"], { stdio: "inherit" });
    await expect.poll(() => members().filter((m) => m.startsWith("3 ")).length, { timeout: 30_000 }).toBe(3);
    service.to(8083);
    execFileSync(pods, ["stop", "pod-a"], { stdio: "inherit" });

    // Pod A left the cluster on its way out; the game is two pods again.
    await expect.poll(() => members().filter((m) => m.startsWith("3 ")).length, { timeout: 60_000 }).toBe(2);
    // Tom's tab came back through the Service (to the new pod) and walked back into the room: both see both again.
    await expect.poll(async () => Object.keys(await cats(tom.page)).sort(), { timeout: 90_000 })
      .toEqual([tomId, felixId].sort());
    await expect.poll(async () => Object.keys(await cats(felix.page)).sort(), { timeout: 90_000 })
      .toEqual([tomId, felixId].sort());
    await expectRoom(tom.page, "Town Square");

    // And the room works across the new pair: chat both ways, a walk seen.
    await say(tom.page, "after, from the new pod");
    await expect.poll(() => chat(felix.page), { timeout: 30_000 }).toContain("after, from the new pod");
    await say(felix.page, "after, from pod b");
    await expect.poll(() => chat(tom.page), { timeout: 30_000 }).toContain("after, from pod b");
    await tapFloor(tom.page, -4, 2);
    await expect.poll(async () => (await cats(felix.page))[tomId]?.x ?? 0).toBeLessThan(-3.5);

    // Felix's own connection, on pod B, never dropped: no console errors on his side.
    expect(felix.errors, "Felix's console errors").toEqual([]);
  } finally {
    await tom.close();
    await felix.close();
    await service.close();
  }
});

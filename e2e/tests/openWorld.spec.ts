import type { HubConnection } from "@microsoft/signalr";
import { adminCookie, expect, hubGuest, test } from "./game";

// The open world end to end: three rooms placed side by side on the world grid (published, then rolled back), two cats
// from Node on the game's hub. Each sees the other while near and not when far, hears chat only when near, and walks
// from one room into the next with no door.

interface Layout { name: string; width: number; depth: number; place?: { x: number; z: number } | null }
interface Detail { published: Layout; versions: { number: number; publishedAt: string | null }[] }
interface Snapshot { roomId: string; chunks?: { x: number; z: number; width: number; depth: number }[] | null; cats: { playerId: string }[] }

const ROOMS = ["town-square", "cafe", "shop"];

async function admin(baseUrl: string, method: string, path: string, body?: unknown): Promise<Response> {
  const { name, value } = adminCookie();
  const response = await fetch(new URL(`/api/admin/rooms${path}`, baseUrl), {
    method,
    headers: { Cookie: `${name}=${value}`, Origin: baseUrl, ...(body ? { "Content-Type": "application/json" } : {}) },
    body: body ? JSON.stringify(body) : undefined,
  });
  expect(response.ok, `${method} ${path}: ${response.status}`).toBe(true);
  return response;
}

/** What a tab was told: the cats that came, the cats that left, the lines it heard and where cats moved. */
function listen(hub: HubConnection) {
  const seen = { joined: [] as string[], left: [] as string[], heard: [] as string[], moved: new Map<string, number>() };
  hub.on("CatJoined", (cat: { playerId: string }) => seen.joined.push(cat.playerId));
  hub.on("CatLeft", (playerId: string) => seen.left.push(playerId));
  hub.on("ChatSaid", (line: { text: string }) => seen.heard.push(line.text));
  hub.on("CatMoved", (playerId: string, x: number) => seen.moved.set(playerId, x));
  return seen;
}

test("cats in the open world see and hear only what is near, and walk from one room into the next", async ({ baseURL }) => {
  test.setTimeout(120_000);
  const base = baseURL!;
  const before = new Map<string, Detail>();
  for (const id of ROOMS) before.set(id, (await (await admin(base, "GET", `/${id}`)).json()) as Detail);

  const tab = { a: await hubGuest(base), b: await hubGuest(base) };
  try {
    // Placed floors are whole 2 m cells: a room that isn't (the shop, 13 by 9) grows to the next one.
    const whole = (metres: number) => Math.ceil(metres / 2) * 2;
    const width = (id: string) => whole(before.get(id)!.published.width);
    const depth = (id: string) => whole(before.get(id)!.published.depth);
    // Side by side along the south edge: Town Square, then the café, then the shop.
    let cell = 0;
    for (const id of ROOMS) {
      await admin(base, "PUT", `/${id}/draft`, { ...before.get(id)!.published, width: width(id), depth: depth(id), place: { x: cell, z: 0 } });
      cell += width(id) / 2;
    }

    await admin(base, "POST", "/town-square/publish?with=cafe&with=shop");

    const heardByA = listen(tab.a.hub);
    const heardByB = listen(tab.b.hub);
    const first = await tab.a.hub.invoke<Snapshot>("EnterWorld", "calico");
    const second = await tab.b.hub.invoke<Snapshot>("EnterWorld", "calico");

    expect(first.roomId, "the cat arrives in Town Square").toBe("town-square");
    expect(first.chunks?.length, "with the three placed rooms drawn around it").toBe(3);
    expect(second.cats.map((cat) => cat.playerId), "the second cat is told of the first").toContain(tab.a.id);
    await expect.poll(() => heardByA.joined).toContain(tab.b.id);

    // A friend's list says which room they are in, not the open world's key.
    await tab.a.hub.invoke("AddFriend", tab.b.id);
    await tab.b.hub.invoke("AcceptFriend", tab.a.id);
    const friends = await tab.a.hub.invoke<{ playerId: string; place: { roomId: string } | null }[]>("GetFriends");
    expect(friends.find((friend) => friend.playerId === tab.b.id)?.place?.roomId, "where the friend is").toBe("town-square");

    // Across the world from each other: out of sight, and what is said there isn't heard.
    const west = 1;
    const east = ROOMS.reduce((sum, id) => sum + width(id), 0) - 1;
    expect(east - west, "the placed rooms are wider than the distance cats see").toBeGreaterThan(40);
    await tab.a.hub.invoke("Move", west, 8);
    await tab.b.hub.invoke("Move", east, 8);
    await expect.poll(() => heardByA.left).toContain(tab.b.id);
    await expect.poll(() => heardByB.left).toContain(tab.a.id);
    await tab.b.hub.invoke("Say", "far away");
    await expect.poll(() => heardByB.heard).toContain("far away");

    // Close together, in the next room over: seen again, and heard.
    const inCafe = width("town-square") + 2;
    await tab.a.hub.invoke("Move", inCafe, 8);
    await tab.b.hub.invoke("Move", inCafe + 3, 8);
    await expect.poll(() => heardByA.joined.filter((id) => id === tab.b.id).length).toBe(2);
    await tab.b.hub.invoke("Say", "close by");
    await expect.poll(() => heardByA.heard).toContain("close by");
    // A's tab gets what it is sent in order: had it been sent the line from across the world, it would be here first.
    expect(heardByA.heard, "a cat across the world isn't heard").toEqual(["close by"]);

    // The café's floor is past Town Square's edge: a cat there isn't held at the seam.
    await tab.b.hub.invoke("Move", inCafe + 5, 8);
    await expect.poll(() => heardByA.moved.get(tab.b.id)).toBe(inCafe + 5);

    // Visiting a friend from across the open world puts the cat beside them, in sight.
    await tab.a.hub.invoke("Move", west, 8);
    const visit = await tab.a.hub.invoke<Snapshot>("GoToFriend", tab.b.id);
    expect(visit.cats.map((cat) => cat.playerId), "the friend is in sight on arrival").toContain(tab.b.id);
  } finally {
    await Promise.all([tab.a.hub.stop(), tab.b.hub.stop()]);
    for (const id of ROOMS) {
      const number = Math.max(...before.get(id)!.versions.filter((version) => version.publishedAt !== null).map((version) => version.number));
      await admin(base, "POST", `/${id}/rollback/${number}`);
    }
  }
});

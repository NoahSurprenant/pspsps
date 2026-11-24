import { describe, expect, it } from "vitest";
import { catchBubble, FishingState, gotAwayMessage, paid, inReachOfPond, pondEdge, reelMessage, type Fishing } from "./fishing";
import type { Player } from "./types";

const pond = { x: -7, z: -3, radius: 2.5 };

describe("pondEdge", () => {
  it("stands the cat at the edge nearest to it, within the server's reach", () => {
    const spot = pondEdge(pond, { x: -7, z: 4 });

    expect(spot.x).toBeCloseTo(-7);
    expect(spot.z).toBeCloseTo(0.3);
    expect(Math.hypot(spot.x - pond.x, spot.z - pond.z)).toBeLessThanOrEqual(pond.radius + 1.5);
  });

  it("works from any side", () => {
    const spot = pondEdge(pond, { x: 3, z: -3 });

    expect(spot.x).toBeCloseTo(-3.7);
    expect(spot.z).toBeCloseTo(-3);
  });

  it("sends a cat on the centre to the edge in front", () => {
    const spot = pondEdge(pond, { x: -7, z: -3 });

    expect(spot.x).toBe(-7);
    expect(spot.z).toBeCloseTo(0.3);
  });
});

describe("reelMessage", () => {
  it("names the catch and the coins", () => {
    expect(reelMessage({ outcome: "caught", fishId: "koi", fishName: "Golden koi", coins: 100, totalCoins: 140 }))
      .toBe("You caught a golden koi! +100 coins");
    expect(reelMessage({ outcome: "caught", fishId: "boot", fishName: "Old boot", coins: 1, totalCoins: 1 }))
      .toBe("You caught an old boot! +1 coin");
  });

  it("says why nothing was caught", () => {
    expect(reelMessage({ outcome: "too-early", fishId: null, fishName: null, coins: 0, totalCoins: null }))
      .toBe("Too soon! It swam off.");
    expect(reelMessage({ outcome: "too-late", fishId: null, fishName: null, coins: 0, totalCoins: null }))
      .toBe("Too slow! It got away.");
  });
});

describe("catchBubble", () => {
  it("shows the fish", () => {
    expect(catchBubble("Goldfish")).toBe("🐟 Goldfish!");
  });
});

describe("FishingState", () => {
  /** Starts a cast that must start. */
  function started(fishing: FishingState): number {
    const cast = fishing.startCast();
    if (cast === undefined) {
      throw new Error("The cast didn't start.");
    }
    return cast;
  }

  function track(): { fishing: FishingState; seen: Fishing[] } {
    const seen: Fishing[] = [];
    return { fishing: new FishingState((state) => seen.push(state)), seen };
  }

  it("goes idle, casting, waiting, bite, reeling, idle", () => {
    const { fishing, seen } = track();

    const cast = fishing.startCast();
    expect(cast).toBeDefined();
    fishing.castDone(cast ?? 0);
    fishing.bite();
    expect(fishing.startReel()).toBe(true);
    fishing.reeled();

    expect(seen).toEqual(["casting", "waiting", "bite", "reeling", "idle"]);
    expect(fishing.state).toBe("idle");
  });

  it("casts once while a cast is in flight", () => {
    const { fishing } = track();

    expect(fishing.startCast()).toBeDefined();
    expect(fishing.startCast()).toBeUndefined();
    expect(fishing.state).toBe("casting");
  });

  it("doesn't wait for a bite on a line dropped while casting", () => {
    const { fishing } = track();
    const cast = started(fishing);

    // A floor click while the cast was on its way: the cat walked off, and the server dropped the line.
    fishing.drop();
    fishing.castDone(cast);

    expect(fishing.state).toBe("idle");
  });

  it("ignores an old cast finishing during a newer one", () => {
    const { fishing } = track();
    const first = started(fishing);
    fishing.drop();
    const second = started(fishing);

    fishing.castFailed(first);
    expect(fishing.state).toBe("casting");
    fishing.castDone(second);
    expect(fishing.state).toBe("waiting");
  });

  it("goes back to idle when the cast fails", () => {
    const { fishing } = track();
    const cast = started(fishing);

    fishing.castFailed(cast);

    expect(fishing.state).toBe("idle");
  });

  it("reels once: a double click or Space and a click send one Reel", () => {
    const { fishing } = track();
    fishing.castDone(started(fishing));
    fishing.bite();

    expect(fishing.startReel()).toBe(true);
    expect(fishing.startReel()).toBe(false);
    expect(fishing.state).toBe("reeling");
  });

  it("reels in early while waiting, but not with no line out", () => {
    const { fishing } = track();
    expect(fishing.startReel()).toBe(false);
    const cast = started(fishing);
    expect(fishing.startReel()).toBe(false);

    fishing.castDone(cast);
    expect(fishing.startReel()).toBe(true);
  });

  it("only bites a line that is waiting", () => {
    const { fishing } = track();
    fishing.bite();
    expect(fishing.state).toBe("idle");

    fishing.startCast();
    fishing.bite();
    expect(fishing.state).toBe("casting");
  });
});

describe("paid", () => {
  const player: Player = { id: "p1", displayName: "Kitty 1234", furColor: "Grey", coins: 40, isGuest: true, hat: null, collar: null };

  it("keeps the coins a catch paid, so the next world picked shows them", () => {
    const after = paid(player, { outcome: "caught", fishId: "koi", fishName: "Golden koi", coins: 100, totalCoins: 140 });

    expect(after).toEqual({ ...player, coins: 140 });
    expect(player.coins).toBe(40);
  });

  it("changes nothing when nothing was caught", () => {
    expect(paid(player, { outcome: "too-late", fishId: null, fishName: null, coins: 0, totalCoins: null }))
      .toBe(player);
  });
});

describe("FishingState, a cast in flight", () => {
  it("is still this cast's until a floor click drops it: then the cast isn't sent", () => {
    const fishing = new FishingState();
    const cast = fishing.startCast() ?? -1;

    expect(fishing.isCasting(cast)).toBe(true);
    fishing.drop();
    expect(fishing.isCasting(cast)).toBe(false);
    const next = fishing.startCast() ?? -1;
    expect(fishing.isCasting(cast)).toBe(false);
    expect(fishing.isCasting(next)).toBe(true);
  });
});

describe("FishingState, a bite not reeled in time", () => {
  it("gets away: back to idle, and the player is told", () => {
    const seen: Fishing[] = [];
    const fishing = new FishingState((state) => seen.push(state));
    fishing.castDone(fishing.startCast() ?? -1);
    fishing.bite();

    expect(fishing.gotAway()).toBe(true);
    expect(fishing.state).toBe("idle");
    expect(seen.at(-1)).toBe("idle");
  });

  it("says nothing when the line was already in, or is being reeled", () => {
    const fishing = new FishingState();
    expect(fishing.gotAway()).toBe(false);

    fishing.castDone(fishing.startCast() ?? -1);
    fishing.bite();
    fishing.startReel();
    expect(fishing.gotAway()).toBe(false);
    expect(fishing.state).toBe("reeling");
  });
});

describe("gotAwayMessage", () => {
  it("tells the player the fish got away", () => {
    expect(gotAwayMessage).toBe("It got away!");
  });
});

describe("inReachOfPond", () => {
  const far = { x: 40, z: 20, radius: 2 };

  it("is true at the server's reach from the edge and not beyond", () => {
    expect(inReachOfPond([pond], { x: -7, z: -3 + 2.5 + 1.5 })).toBe(true);
    expect(inReachOfPond([pond], { x: -7, z: -3 + 2.5 + 1.6 })).toBe(false);
  });

  it("looks at every pond and is false when there is none", () => {
    expect(inReachOfPond([pond, far], { x: 41, z: 20 })).toBe(true);
    expect(inReachOfPond([pond, far], { x: 0, z: 0 })).toBe(false);
    expect(inReachOfPond([], { x: 0, z: 0 })).toBe(false);
  });
});

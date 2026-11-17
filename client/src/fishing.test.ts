import { describe, expect, it } from "vitest";
import { catchBubble, pondEdge, reelMessage } from "./fishing";

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
    expect(reelMessage({ outcome: "too-early", fishId: null, fishName: null, coins: 0, totalCoins: 0 }))
      .toBe("Too soon! It swam off.");
    expect(reelMessage({ outcome: "too-late", fishId: null, fishName: null, coins: 0, totalCoins: 0 }))
      .toBe("Too slow! It got away.");
  });
});

describe("catchBubble", () => {
  it("shows the fish", () => {
    expect(catchBubble("Goldfish")).toBe("🐟 Goldfish!");
  });
});

import { doorSpot, type CanvasDoor, type CanvasLayout, type CanvasProp, type Spot } from "./roomCanvas";

/** Mirrors PropPlacement in the domain: the room a cat takes up, how far in a door stands, its size, and how far in a cat lands. */
const CAT_ROOM = 0.6;
const DOOR_INSET = 1;
const DOOR_RADIUS = 0.8;
const STEP_IN = 0.75;
const POND_REACH = 1.5;

const gap = (a: Spot, b: Spot): number => Math.hypot(a.x - b.x, a.z - b.z);

interface Stop { label: string; spot: Spot; clear: number; on: string }

/** Where a door really stands on the floor: one metre in from its edge (the canvas draws it on the edge). */
export function doorFloorSpot(layout: Pick<CanvasLayout, "width" | "depth">, door: Pick<CanvasDoor, "edge" | "offset">): Spot {
  const edge = doorSpot(layout, door);
  switch (door.edge) {
    case "North": return { x: edge.x, z: edge.z - DOOR_INSET };
    case "South": return { x: edge.x, z: edge.z + DOOR_INSET };
    case "East": return { x: edge.x - DOOR_INSET, z: edge.z };
    default: return { x: edge.x + DOOR_INSET, z: edge.z };
  }
}

function arrival(layout: CanvasLayout, door: CanvasDoor): Spot {
  const spot = doorFloorSpot(layout, door);
  const inward = DOOR_RADIUS + STEP_IN;
  switch (door.edge) {
    case "North": return { x: spot.x, z: spot.z - inward };
    case "South": return { x: spot.x, z: spot.z + inward };
    case "East": return { x: spot.x - inward, z: spot.z };
    default: return { x: spot.x + inward, z: spot.z };
  }
}

function stopsOf(layout: CanvasLayout): Stop[] {
  const stops: Stop[] = [{ label: "the spawn point", spot: layout.spawn, clear: CAT_ROOM, on: "is on the spawn point" }];
  for (const door of layout.doors) {
    stops.push({ label: `the door ${door.id}`, spot: doorFloorSpot(layout, door), clear: DOOR_RADIUS + CAT_ROOM, on: `is on the door ${door.id}` });
    const wayIn = `the way in by the door ${door.id}`;
    stops.push({ label: wayIn, spot: arrival(layout, door), clear: CAT_ROOM, on: `is on ${wayIn}` });
  }
  const pond = layout.pond;
  if (pond) stops.push({ label: "the pond", spot: pond.center, clear: pond.radius + POND_REACH, on: "is in the pond or where cats fish from" });
  return stops;
}

function distanceToWalk(spot: Spot, from: Spot, to: Spot): number {
  const dx = to.x - from.x;
  const dz = to.z - from.z;
  const lengthSquared = dx * dx + dz * dz;
  const along = lengthSquared === 0 ? 0 : Math.max(0, Math.min(1, ((spot.x - from.x) * dx + (spot.z - from.z) * dz) / lengthSquared));
  return gap(spot, { x: from.x + along * dx, z: from.z + along * dz });
}

/** What is wrong with where the prop at `index` stands, as the server will judge it when the draft is published; empty when it keeps clear. */
export function propProblems(layout: CanvasLayout, index: number): string[] {
  const prop: CanvasProp | undefined = layout.props[index];
  if (!prop) return [];
  const problems: string[] = [];
  if (prop.radius <= 0) problems.push("has no size");
  if (Math.abs(prop.spot.x) + prop.radius > layout.width / 2 || Math.abs(prop.spot.z) + prop.radius > layout.depth / 2) problems.push("is partly off the floor");
  const stops = stopsOf(layout);
  for (const stop of stops) {
    if (gap(prop.spot, stop.spot) < prop.radius + stop.clear) problems.push(stop.on);
  }
  for (let a = 0; a < stops.length; a++) {
    for (let b = a + 1; b < stops.length; b++) {
      const from = stops[a];
      const to = stops[b];
      if (from && to && distanceToWalk(prop.spot, from.spot, to.spot) < prop.radius + CAT_ROOM) problems.push(`is on the walk from ${from.label} to ${to.label}`);
    }
  }
  layout.props.forEach((other, i) => {
    if (i !== index && gap(prop.spot, other.spot) < prop.radius + other.radius) problems.push(`overlaps the ${other.kind}`);
  });
  return problems;
}

/** The indexes of the props that are in the way, for drawing them red. */
export function propsInTheWay(layout: CanvasLayout): number[] {
  return layout.props.map((_, i) => i).filter((i) => propProblems(layout, i).length > 0);
}

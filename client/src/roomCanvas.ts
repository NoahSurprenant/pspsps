import { el } from "./dom";

export interface Spot { x: number; z: number }
export interface CanvasProp { kind: string; spot: Spot; radius: number; facing?: number }
export interface CanvasDoor { id: string; edge: "North" | "East" | "South" | "West"; offset: number; toRoom: string; toDoor: string }
export interface CanvasLayout {
  name: string;
  width: number;
  depth: number;
  spawn: Spot;
  capacity: number;
  props: CanvasProp[];
  doors: CanvasDoor[];
  [other: string]: unknown;
}

export type Picked = { kind: "prop"; index: number } | { kind: "door"; index: number } | { kind: "spawn" };

/** Pixels per metre that fits the floor in the canvas. */
export function scaleFor(layout: Pick<CanvasLayout, "width" | "depth">, pixelsWide: number, pixelsHigh: number): number {
  return Math.min(pixelsWide / layout.width, pixelsHigh / layout.depth);
}

/** Floor position to canvas pixel: north (the back of the room) at the top, east on the right. */
export function toPixel(layout: Pick<CanvasLayout, "width" | "depth">, scale: number, spot: Spot): { px: number; py: number } {
  return { px: (spot.x + layout.width / 2) * scale, py: (layout.depth / 2 - spot.z) * scale };
}

export function fromPixel(layout: Pick<CanvasLayout, "width" | "depth">, scale: number, px: number, py: number): Spot {
  return { x: px / scale - layout.width / 2, z: layout.depth / 2 - py / scale };
}

/** Where a door stands on the floor edge it is on. */
export function doorSpot(layout: Pick<CanvasLayout, "width" | "depth">, door: Pick<CanvasDoor, "edge" | "offset">): Spot {
  switch (door.edge) {
    case "North": return { x: door.offset, z: layout.depth / 2 };
    case "South": return { x: door.offset, z: -layout.depth / 2 };
    case "East": return { x: layout.width / 2, z: door.offset };
    default: return { x: -layout.width / 2, z: door.offset };
  }
}

/** The nearest floor edge to a spot, and the offset along it: where a dragged door lands. */
export function doorAtEdge(layout: Pick<CanvasLayout, "width" | "depth">, spot: Spot): Pick<CanvasDoor, "edge" | "offset"> {
  const gaps: [CanvasDoor["edge"], number][] = [
    ["North", layout.depth / 2 - spot.z],
    ["South", spot.z + layout.depth / 2],
    ["East", layout.width / 2 - spot.x],
    ["West", spot.x + layout.width / 2],
  ];
  const [edge] = gaps.reduce((best, gap) => (gap[1] < best[1] ? gap : best));
  const along = edge === "North" || edge === "South" ? spot.x : spot.z;
  const half = (edge === "North" || edge === "South" ? layout.width : layout.depth) / 2;
  return { edge, offset: round(Math.max(-half, Math.min(half, along))) };
}

const round = (value: number): number => Math.round(value * 100) / 100;

/** What is under a floor spot: doors and the spawn first (they are small), then the nearest prop whose circle holds it. */
export function pick(layout: CanvasLayout, spot: Spot, reach = 0.6): Picked | null {
  const near = (a: Spot, b: Spot, within: number): boolean => Math.hypot(a.x - b.x, a.z - b.z) <= within;
  const door = layout.doors.findIndex((d) => near(doorSpot(layout, d), spot, reach));
  if (door >= 0) return { kind: "door", index: door };
  if (near(layout.spawn, spot, reach)) return { kind: "spawn" };
  let best = -1;
  let bestGap = Infinity;
  layout.props.forEach((prop, index) => {
    const gap = Math.hypot(prop.spot.x - spot.x, prop.spot.z - spot.z);
    if (gap <= Math.max(prop.radius, 0.3) && gap < bestGap) { best = index; bestGap = gap; }
  });
  return best >= 0 ? { kind: "prop", index: best } : null;
}

/** The layout with what was picked moved to a spot (a door slides to the nearest edge); the layout itself is not changed. */
export function moveTo(layout: CanvasLayout, picked: Picked, spot: Spot): CanvasLayout {
  const clamp = (value: number, half: number): number => round(Math.max(-half, Math.min(half, value)));
  const inside = { x: clamp(spot.x, layout.width / 2), z: clamp(spot.z, layout.depth / 2) };
  switch (picked.kind) {
    case "spawn": return { ...layout, spawn: inside };
    case "prop": return { ...layout, props: layout.props.map((p, i) => (i === picked.index ? { ...p, spot: inside } : p)) };
    case "door": return { ...layout, doors: layout.doors.map((d, i) => (i === picked.index ? { ...d, ...doorAtEdge(layout, spot) } : d)) };
  }
}

export function addProp(layout: CanvasLayout, kind: string, spot: Spot, radius = 1): CanvasLayout {
  return { ...layout, props: [...layout.props, { kind, spot: { x: round(spot.x), z: round(spot.z) }, radius }] };
}

export function removePicked(layout: CanvasLayout, picked: Picked): CanvasLayout {
  if (picked.kind === "prop") return { ...layout, props: layout.props.filter((_, i) => i !== picked.index) };
  if (picked.kind === "door") return { ...layout, doors: layout.doors.filter((_, i) => i !== picked.index) };
  return layout;
}

/** A starting layout for a new room: an empty floor with a spawn in the middle, for doors and props to be added to. */
export function newRoomTemplate(name: string): CanvasLayout {
  return { name, width: 12, depth: 10, spawn: { x: 0, z: 0 }, capacity: 12, props: [], doors: [] };
}

export function isLayout(value: unknown): value is CanvasLayout {
  const v = value as Partial<CanvasLayout> | null;
  return !!v && typeof v.width === "number" && typeof v.depth === "number" && Array.isArray(v.props) && Array.isArray(v.doors)
    && !!v.spawn && typeof v.spawn.x === "number";
}

const COLOURS: Record<Picked["kind"], string> = { prop: "#6b8e5a", door: "#c0762b", spawn: "#3b6fb6" };

/**
 * A top-down view of a room's layout on a canvas: drag a prop, a door or the spawn to move it, click empty floor to add
 * the chosen kind of prop, press Delete to remove what is selected. Every change goes out through onChange as a new layout.
 */
export function roomCanvas(
  initial: CanvasLayout,
  kinds: readonly string[],
  onChange: (layout: CanvasLayout) => void,
): HTMLElement {
  let layout = initial;
  let selected: Picked | null = null;
  let dragging = false;
  const canvas = el("canvas", {});
  canvas.width = 640;
  canvas.height = 480;
  canvas.tabIndex = 0;
  canvas.setAttribute("aria-label", "Top-down view of the room");
  canvas.style.maxWidth = "100%";
  canvas.style.touchAction = "none";
  const kind = el("select", {});
  kind.setAttribute("aria-label", "Kind of prop to add");
  for (const name of kinds) kind.append(el("option", { text: name }));
  const info = el("p", { className: "quiet", text: "Click empty floor to add a prop, drag to move, Delete to remove." });

  const geometry = (): number => scaleFor(layout, canvas.width, canvas.height);
  const spotOf = (event: PointerEvent): Spot => {
    const box = canvas.getBoundingClientRect();
    const ratio = canvas.width / box.width;
    return fromPixel(layout, geometry(), (event.clientX - box.left) * ratio, (event.clientY - box.top) * ratio);
  };

  function draw(): void {
    const context = canvas.getContext("2d");
    if (!context) return;
    const scale = geometry();
    context.clearRect(0, 0, canvas.width, canvas.height);
    context.fillStyle = "#efe6d2";
    context.fillRect(0, 0, layout.width * scale, layout.depth * scale);
    const mark = (spot: Spot, radius: number, kindOf: Picked["kind"], label: string, chosen: boolean): void => {
      const { px, py } = toPixel(layout, scale, spot);
      context.beginPath();
      context.arc(px, py, Math.max(radius * scale, 6), 0, Math.PI * 2);
      context.fillStyle = COLOURS[kindOf] + "99";
      context.fill();
      context.lineWidth = chosen ? 3 : 1;
      context.strokeStyle = chosen ? "#000" : COLOURS[kindOf];
      context.stroke();
      context.fillStyle = "#222";
      context.font = "11px sans-serif";
      context.fillText(label, px + 4, py - 4);
    };
    layout.props.forEach((p, i) => mark(p.spot, p.radius, "prop", p.kind, selected?.kind === "prop" && selected.index === i));
    layout.doors.forEach((d, i) => mark(doorSpot(layout, d), 0.4, "door", `${d.id} → ${d.toRoom}`, selected?.kind === "door" && selected.index === i));
    mark(layout.spawn, 0.3, "spawn", "spawn", selected?.kind === "spawn");
  }

  const change = (next: CanvasLayout): void => { layout = next; draw(); onChange(layout); };

  canvas.addEventListener("pointerdown", (event) => {
    const spot = spotOf(event);
    const hit = pick(layout, spot);
    if (hit) {
      selected = hit;
      dragging = true;
      canvas.setPointerCapture(event.pointerId);
      info.textContent = hit.kind === "prop" ? `${layout.props[hit.index]?.kind ?? ""} selected.` : `${hit.kind} selected.`;
      draw();
    } else {
      selected = null;
      change(addProp(layout, kind.value, spot));
    }
  });
  canvas.addEventListener("pointermove", (event) => {
    if (dragging && selected) { layout = moveTo(layout, selected, spotOf(event)); draw(); }
  });
  canvas.addEventListener("pointerup", () => {
    if (dragging) { dragging = false; onChange(layout); }
  });
  canvas.addEventListener("keydown", (event) => {
    if ((event.key === "Delete" || event.key === "Backspace") && selected) {
      event.preventDefault();
      const gone = selected;
      selected = null;
      change(removePicked(layout, gone));
    }
  });

  draw();
  return el("div", {}, canvas, el("label", { text: "Add: " }, kind), info);
}

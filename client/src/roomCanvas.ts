import { el } from "./dom";
import { propProblems, propsInTheWay } from "./roomClearance";

export interface Spot { x: number; z: number }
export interface CanvasProp { kind: string; spot: Spot; radius: number; facing?: number }
export interface CanvasDoor { id: string; edge: "North" | "East" | "South" | "West"; offset: number; toRoom: string; toDoor: string }
export interface CanvasPond { center: Spot; radius: number }
export interface CanvasLayout {
  name: string;
  width: number;
  depth: number;
  spawn: Spot;
  capacity: number;
  props: CanvasProp[];
  doors: CanvasDoor[];
  pond?: CanvasPond | null;
  [other: string]: unknown;
}

export type Picked = { kind: "prop"; index: number } | { kind: "door"; index: number } | { kind: "spawn" } | { kind: "pond" };

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
  const north: [CanvasDoor["edge"], number] = ["North", layout.depth / 2 - spot.z];
  const gaps: [CanvasDoor["edge"], number][] = [
    north,
    ["South", spot.z + layout.depth / 2],
    ["East", layout.width / 2 - spot.x],
    ["West", spot.x + layout.width / 2],
  ];
  const [edge] = gaps.reduce((best, gap) => (gap[1] < best[1] ? gap : best), north);
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
  if (layout.pond && near(layout.pond.center, spot, Math.max(layout.pond.radius, 0.3))) return { kind: "pond" };
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
    case "pond": return layout.pond ? { ...layout, pond: { ...layout.pond, center: inside } } : layout;
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

const COLOURS: Record<Picked["kind"], string> = { prop: "#6b8e5a", door: "#c0762b", spawn: "#3b6fb6", pond: "#4aa3c8" };

const OPPOSITE: Record<CanvasDoor["edge"], CanvasDoor["edge"]> = { North: "South", South: "North", East: "West", West: "East" };

/** The door that leads back through a new one: on the opposite edge of the other room, at the same offset, pointing here. */
export function backDoor(door: CanvasDoor, thisRoom: string): CanvasDoor {
  return { id: thisRoom, edge: OPPOSITE[door.edge], offset: door.offset, toRoom: thisRoom, toDoor: door.id };
}

/** A new door on an edge leading to a door of another room (its ID is that room's), to be added with its back door. */
export function newDoor(layout: CanvasLayout, edge: CanvasDoor["edge"], toRoom: string, thisRoom: string): CanvasDoor {
  const id = layout.doors.some((d) => d.id === toRoom) ? `${toRoom}-${layout.doors.length + 1}` : toRoom;
  return { id, edge, offset: 0, toRoom, toDoor: thisRoom };
}

/** Keeps what was done and undone: each change is pushed, and undo and redo walk back and forth along them. */
export class History<T> {
  private past: T[] = [];
  private future: T[] = [];

  constructor(private current: T) {}

  get value(): T { return this.current; }
  get canUndo(): boolean { return this.past.length > 0; }
  get canRedo(): boolean { return this.future.length > 0; }

  push(next: T): void {
    this.past.push(this.current);
    this.current = next;
    this.future = [];
  }

  undo(): T {
    const back = this.past.pop();
    if (back !== undefined) { this.future.push(this.current); this.current = back; }
    return this.current;
  }

  redo(): T {
    const forward = this.future.pop();
    if (forward !== undefined) { this.past.push(this.current); this.current = forward; }
    return this.current;
  }
}

export function setProp(layout: CanvasLayout, index: number, change: Partial<Pick<CanvasProp, "radius" | "facing">>): CanvasLayout {
  return { ...layout, props: layout.props.map((p, i) => (i === index ? { ...p, ...change } : p)) };
}

/** Puts a pond in the room (in the middle, or where given), changes its size, or takes it away with null. */
export function setPond(layout: CanvasLayout, pond: CanvasPond | null): CanvasLayout {
  return { ...layout, pond };
}

export interface CanvasOptions {
  /** Called to add a door and, with it, the door back in the other room; the canvas adds this room's door itself when it resolves. */
  addDoorPair?: (door: CanvasDoor, back: CanvasDoor) => Promise<void>;
  /** Rooms a door may lead to. */
  rooms?: readonly string[];
  roomId?: string;
}

const number = (label: string, value: number, step: number, apply: (value: number) => void): HTMLLabelElement => {
  const input = el("input", {});
  input.type = "number";
  input.step = String(step);
  input.value = String(value);
  input.style.width = "5em";
  input.addEventListener("change", () => { const n = Number(input.value); if (Number.isFinite(n)) apply(n); });
  return el("label", { text: `${label} ` }, input);
};

/**
 * A top-down view of a room's layout on a canvas: drag a prop, a door, the pond or the spawn to move it, click empty floor
 * to add the chosen kind of prop, press Delete to remove what is selected, Ctrl+Z and Ctrl+Y to undo and redo. Props that
 * would stop the room being published are red, and say why when selected. Every change goes out through onChange.
 */
export function roomCanvas(
  initial: CanvasLayout,
  kinds: readonly string[],
  onChange: (layout: CanvasLayout) => void,
  options: CanvasOptions = {},
): HTMLElement {
  const history = new History(initial);
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
  const controls = el("div", {});
  const warnings = el("ul", { className: "list" });

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
    const blocked = new Set(propsInTheWay(layout));
    context.clearRect(0, 0, canvas.width, canvas.height);
    context.fillStyle = "#efe6d2";
    context.fillRect(0, 0, layout.width * scale, layout.depth * scale);
    const mark = (spot: Spot, radius: number, kindOf: Picked["kind"], label: string, chosen: boolean, bad = false): void => {
      const { px, py } = toPixel(layout, scale, spot);
      const colour = bad ? "#c0392b" : COLOURS[kindOf];
      context.beginPath();
      context.arc(px, py, Math.max(radius * scale, 6), 0, Math.PI * 2);
      context.fillStyle = colour + "99";
      context.fill();
      context.lineWidth = chosen ? 3 : 1;
      context.strokeStyle = chosen ? "#000" : colour;
      context.stroke();
      context.fillStyle = "#222";
      context.font = "11px sans-serif";
      context.fillText(label, px + 4, py - 4);
    };
    if (layout.pond) mark(layout.pond.center, layout.pond.radius, "pond", "pond", selected?.kind === "pond");
    layout.props.forEach((p, i) => mark(p.spot, p.radius, "prop", p.kind, selected?.kind === "prop" && selected.index === i, blocked.has(i)));
    layout.doors.forEach((d, i) => mark(doorSpot(layout, d), 0.4, "door", `${d.id} → ${d.toRoom}`, selected?.kind === "door" && selected.index === i));
    mark(layout.spawn, 0.3, "spawn", "spawn", selected?.kind === "spawn");
  }

  const undo = el("button", { text: "Undo" });
  const redo = el("button", { text: "Redo" });
  const refreshControls = (): void => {
    undo.disabled = !history.canUndo;
    redo.disabled = !history.canRedo;
    const parts: HTMLElement[] = [];
    if (selected?.kind === "prop") {
      const index = selected.index;
      const prop = layout.props[index];
      if (prop) {
        parts.push(
          number("Radius", prop.radius, 0.1, (radius) => change(setProp(layout, index, { radius }))),
          number("Facing (turns)", prop.facing ?? 0, 0.05, (facing) => change(setProp(layout, index, { facing }))));
        warnings.replaceChildren(...propProblems(layout, index).map((text) => el("li", { className: "quiet", text: `The ${prop.kind} ${text}.` })));
      }
    } else {
      warnings.replaceChildren();
    }
    const pond = layout.pond;
    parts.push(pond
      ? el("span", {}, number("Pond radius", pond.radius, 0.25, (radius) => change(setPond(layout, { ...pond, radius }))), button("Remove pond", () => change(setPond(layout, null))))
      : button("Add pond", () => change(setPond(layout, { center: { x: 0, z: 0 }, radius: 2 }))));
    if (options.addDoorPair && options.rooms && options.roomId) parts.push(doorPairForm(options.addDoorPair, options.rooms, options.roomId));
    controls.replaceChildren(...parts);
  };

  function doorPairForm(add: NonNullable<CanvasOptions["addDoorPair"]>, rooms: readonly string[], roomId: string): HTMLElement {
    const target = el("select", {});
    target.setAttribute("aria-label", "Room the new door leads to");
    for (const name of rooms.filter((r) => r !== roomId)) target.append(el("option", { text: name }));
    const edge = el("select", {});
    edge.setAttribute("aria-label", "Edge of this room for the new door");
    for (const name of ["North", "East", "South", "West"]) edge.append(el("option", { text: name }));
    return el("span", {}, el("label", { text: "New door to " }, target), el("label", { text: " on " }, edge),
      button("Add door with its way back", () => {
        if (!target.value) return;
        const door = newDoor(layout, edge.value as CanvasDoor["edge"], target.value, roomId);
        void add(door, backDoor(door, roomId)).then(() => change({ ...layout, doors: [...layout.doors, door] }));
      }));
  }

  function button(text: string, click: () => void): HTMLButtonElement {
    const b = el("button", { text });
    b.addEventListener("click", click);
    return b;
  }

  const show = (next: CanvasLayout): void => { layout = next; selected = null; draw(); refreshControls(); onChange(layout); };
  function change(next: CanvasLayout): void { history.push(next); layout = next; draw(); refreshControls(); onChange(layout); }
  undo.addEventListener("click", () => show(history.undo()));
  redo.addEventListener("click", () => show(history.redo()));

  canvas.addEventListener("pointerdown", (event) => {
    const spot = spotOf(event);
    const hit = pick(layout, spot);
    if (hit) {
      selected = hit;
      dragging = true;
      canvas.setPointerCapture(event.pointerId);
      info.textContent = hit.kind === "prop" ? `${layout.props[hit.index]?.kind ?? ""} selected.` : `${hit.kind} selected.`;
      draw();
      refreshControls();
    } else {
      selected = null;
      change(addProp(layout, kind.value, spot));
    }
  });
  canvas.addEventListener("pointermove", (event) => {
    if (dragging && selected) { layout = moveTo(layout, selected, spotOf(event)); draw(); }
  });
  canvas.addEventListener("pointerup", () => {
    if (dragging) { dragging = false; history.push(layout); refreshControls(); onChange(layout); }
  });
  canvas.addEventListener("keydown", (event) => {
    const key = event.key.toLowerCase();
    if ((event.ctrlKey || event.metaKey) && (key === "z" || key === "y")) {
      event.preventDefault();
      show(key === "y" || event.shiftKey ? history.redo() : history.undo());
    } else if ((key === "delete" || key === "backspace") && selected) {
      event.preventDefault();
      const gone = selected;
      selected = null;
      change(removePicked(layout, gone));
    }
  });

  draw();
  refreshControls();
  return el("div", {}, canvas, el("label", { text: "Add: " }, kind), undo, redo, info, warnings, controls);
}

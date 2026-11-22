import { el } from "./dom";
import { followSize } from "./followSize";
import { FILTERS, MARKER_HEIGHT, filterLabel, isOpen, matches, placeName, screenPoint, shouldTravel, type Filter } from "./islandMap";
import type { IslandScene } from "./islandScene";
import { WHOLE, ZOOM, panBy, project, zoomAt, type Frame } from "./mapZoom";
import type { MapPlaceView, RoomSnapshot } from "./types";

/**
 * The island: a 3D picture with a button on every place, a tab for each kind of place, and a line saying what the chosen
 * place is. A place that isn't built yet is on the map but can't be entered.
 */
export function drawMap(room: RoomSnapshot, travel: (roomId: string) => void): HTMLElement {
  const places = room.worldMap?.places;
  if (!places?.length) {
    // A snapshot from an older pod has the public destinations only.
    const map = el("div", { className: "world-map" });
    for (const { id, name } of [{ id: room.roomId, name: room.roomName }, ...room.rooms]) {
      const button = el("button", { className: "map-place", text: name });
      button.type = "button";
      button.dataset.place = id;
      button.disabled = id === room.roomId;
      button.addEventListener("click", () => travel(id));
      map.append(button);
    }
    return map;
  }

  const wrap = el("div", { className: "world-map" });
  const canvas = el("canvas", { className: "map-canvas" });
  canvas.setAttribute("aria-hidden", "true");
  const picture = el("div", { className: "map-picture" }, canvas);
  const zoomButton = (label: string, text: string) => {
    const button = el("button", { className: "map-zoom-button", text });
    button.type = "button";
    button.setAttribute("aria-label", label);
    return button;
  };
  const zoomIn = zoomButton("Zoom in", "+");
  const zoomOut = zoomButton("Zoom out", "\u2212");
  const zoomControls = el("div", { className: "map-zoom" }, zoomIn, zoomOut);
  zoomIn.addEventListener("click", () => setFrame(zoomAt(frame, ZOOM.step)));
  zoomOut.addEventListener("click", () => setFrame(zoomAt(frame, 1 / ZOOM.step)));
  const caption = el("p", { className: "map-caption" });
  caption.setAttribute("aria-live", "polite");
  const go = el("button", { className: "map-go" });
  go.type = "button";
  const info = el("div", { className: "map-info" }, caption, go);
  let selected: MapPlaceView | null = null;
  let filter: Filter = "all";
  let island: IslandScene | undefined;

  const markers = new Map<string, HTMLButtonElement>();
  const home = new Map<string, { left: number; top: number }>();
  let frame: Frame = WHOLE;
  const setFrame = (next: Frame) => {
    frame = next;
    for (const [id, marker] of markers) {
      const at = project(home.get(id)?.left ?? 0, home.get(id)?.top ?? 0, frame);
      marker.style.left = `${at.left}%`;
      marker.style.top = `${at.top}%`;
    }
    zoomIn.disabled = frame.zoom >= ZOOM.max;
    zoomOut.disabled = frame.zoom <= ZOOM.min;
    island?.view(frame);
  };
  const show = (place: MapPlaceView | null) => {
    selected = place;
    for (const [id, marker] of markers) marker.classList.toggle("selected", id === place?.id);
    go.hidden = !place || !isOpen(place) || place.roomId === room.roomId;
    if (!place) {
      caption.textContent = "Choose a place on the island.";
      return;
    }
    let state = "Coming soon.";
    if (place.roomId === room.roomId) state = "You are here.";
    else if (isOpen(place)) state = "";
    caption.textContent = [place.name, place.blurb, state].filter(Boolean).join(" · ");
    go.textContent = `Go to ${place.name}`;
  };
  go.addEventListener("click", () => selected?.roomId && travel(selected.roomId));

  for (const place of places) {
    const marker = el("button", { className: "map-place" }, el("span", { className: "map-dot" }), el("span", { className: "map-name", text: place.name }));
    marker.type = "button";
    marker.dataset.place = place.id;
    marker.dataset.room = place.roomId ?? "";
    marker.dataset.category = place.category;
    marker.classList.toggle("map-closed", !isOpen(place));
    marker.setAttribute("aria-label", placeName(place, room.roomId));
    if (place.roomId === room.roomId) marker.setAttribute("aria-current", "location");
    home.set(place.id, screenPoint(place.x, place.z, MARKER_HEIGHT));
    marker.addEventListener("click", (event) => {
      const touch = (event as PointerEvent).pointerType ?? "";
      const again = selected?.id === place.id;
      show(place);
      if (place.roomId && place.roomId !== room.roomId && shouldTravel(again, touch)) travel(place.roomId);
    });
    marker.addEventListener("pointerenter", (event) => {
      if (event.pointerType === "mouse" && !selected) caption.textContent = place.name;
    });
    markers.set(place.id, marker);
    picture.append(marker);
  }

  const tabs = el("div", { className: "map-tabs" });
  tabs.setAttribute("role", "group");
  tabs.setAttribute("aria-label", "Kinds of place");
  for (const kind of FILTERS) {
    const tab = el("button", { className: "map-tab", text: filterLabel(kind) });
    tab.type = "button";
    tab.dataset.filter = kind;
    tab.setAttribute("aria-pressed", String(kind === filter));
    tab.addEventListener("click", () => {
      filter = kind;
      for (const other of tabs.querySelectorAll<HTMLButtonElement>(".map-tab")) {
        other.setAttribute("aria-pressed", String(other.dataset.filter === kind));
      }
      for (const place of places) markers.get(place.id)?.classList.toggle("map-dim", !matches(place, kind));
      island?.filter(kind);
    });
    tabs.append(tab);
  }

  followGestures(picture, () => frame, setFrame);
  picture.append(zoomControls);
  setFrame(WHOLE);
  show(null);
  wrap.append(tabs, picture, info);
  void startPicture(canvas, places).then((started) => {
    island = started;
    island?.filter(filter);
    island?.view(frame);
  });
  return wrap;
}

/** The 3D picture loads when the map opens, not with the game; without WebGL the buttons still work. */
async function startPicture(canvas: HTMLCanvasElement, places: MapPlaceView[]): Promise<IslandScene | undefined> {
  try {
    const [{ Engine }, { drawIsland }] = await Promise.all([import("@babylonjs/core/Engines/engine"), import("./islandScene")]);
    if (!canvas.isConnected) return undefined;
    const engine = new Engine(canvas, true, { canvasTabIndex: -1 });
    const island = drawIsland(engine, places);
    const stopFollowing = followSize(canvas, () => island.resize());
    // The sheet takes its canvas away when it closes.
    const watch = new MutationObserver(() => {
      if (!canvas.isConnected) {
        watch.disconnect();
        stopFollowing();
        island.dispose();
      }
    });
    watch.observe(document.body, { childList: true, subtree: true });
    return island;
  } catch {
    return undefined;
  }
}

/** The wheel zooms, two fingers pinch, one finger or the mouse drags; a drag does not count as a tap on a place. */
function followGestures(picture: HTMLElement, frame: () => Frame, set: (frame: Frame) => void): void {
  const touches = new Map<number, { x: number; y: number }>();
  let pinch = 0;
  let moved = 0;
  const percent = (event: { clientX: number; clientY: number }) => {
    const box = picture.getBoundingClientRect();
    return { x: (100 * (event.clientX - box.left)) / (box.width || 1), y: (100 * (event.clientY - box.top)) / (box.height || 1), box };
  };
  const spread = () => {
    const [a, b] = [...touches.values()];
    return a && b ? Math.hypot(a.x - b.x, a.y - b.y) : 0;
  };
  picture.addEventListener("wheel", (event) => {
    event.preventDefault();
    const at = percent(event);
    set(zoomAt(frame(), event.deltaY < 0 ? ZOOM.step : 1 / ZOOM.step, at.x, at.y));
  }, { passive: false });
  picture.addEventListener("pointerdown", (event) => {
    touches.set(event.pointerId, { x: event.clientX, y: event.clientY });
    moved = 0;
    pinch = spread();
  });
  picture.addEventListener("pointermove", (event) => {
    const last = touches.get(event.pointerId);
    if (!last) return;
    const now = { x: event.clientX, y: event.clientY };
    touches.set(event.pointerId, now);
    const box = picture.getBoundingClientRect();
    if (touches.size === 2) {
      const next = spread();
      if (pinch > 0 && next > 0) {
        const mid = percent({ clientX: [...touches.values()].reduce((sum, t) => sum + t.x, 0) / 2, clientY: [...touches.values()].reduce((sum, t) => sum + t.y, 0) / 2 });
        set(zoomAt(frame(), next / pinch, mid.x, mid.y));
      }
      pinch = next;
      moved += 10;
    } else if (touches.size === 1 && frame().zoom > ZOOM.min) {
      moved += Math.abs(now.x - last.x) + Math.abs(now.y - last.y);
      set(panBy(frame(), (100 * (now.x - last.x)) / (box.width || 1), (100 * (now.y - last.y)) / (box.height || 1)));
    }
  });
  const lift = (event: PointerEvent) => {
    touches.delete(event.pointerId);
    pinch = spread();
  };
  picture.addEventListener("pointerup", lift);
  picture.addEventListener("pointercancel", lift);
  picture.addEventListener("pointerleave", lift);
  picture.addEventListener("click", (event) => {
    if (moved > 8) {
      event.stopPropagation();
      event.preventDefault();
    }
    moved = 0;
  }, true);
}

import { el } from "./dom";
import { FILTERS, filterLabel, isOpen, matches, placeName, screenPoint, shouldTravel, type Filter } from "./islandMap";
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
  const caption = el("p", { className: "map-caption" });
  caption.setAttribute("aria-live", "polite");
  const go = el("button", { className: "map-go" });
  go.type = "button";
  const info = el("div", { className: "map-info" }, caption, go);
  let selected: MapPlaceView | null = null;
  let filter: Filter = "all";
  let island: { filter(filter: Filter): void; resize(): void; dispose(): void } | undefined;

  const markers = new Map<string, HTMLButtonElement>();
  const show = (place: MapPlaceView | null) => {
    selected = place;
    for (const [id, marker] of markers) marker.classList.toggle("selected", id === place?.id);
    go.hidden = !place || !isOpen(place) || place.roomId === room.roomId;
    if (!place) {
      caption.textContent = "Choose a place on the island.";
      return;
    }
    const state = place.roomId === room.roomId ? "You are here." : isOpen(place) ? "" : "Coming soon.";
    caption.textContent = [place.name, place.blurb, state].filter(Boolean).join(" · ");
    go.textContent = `Go to ${place.name}`;
  };
  go.addEventListener("click", () => selected?.roomId && travel(selected.roomId));

  for (const place of places) {
    const marker = el("button", { className: "map-place" }, el("span", { className: "map-dot" }), el("span", { className: "map-name", text: place.name }));
    marker.type = "button";
    marker.dataset.place = place.id;
    marker.dataset.category = place.category;
    marker.classList.toggle("map-closed", !isOpen(place));
    marker.setAttribute("aria-label", placeName(place, room.roomId));
    if (place.roomId === room.roomId) marker.setAttribute("aria-current", "location");
    const { left, top } = screenPoint(place.x, place.z, 3);
    marker.style.left = `${left}%`;
    marker.style.top = `${top}%`;
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

  show(null);
  wrap.append(tabs, picture, info);
  void startPicture(canvas, places).then((started) => {
    island = started;
    island?.filter(filter);
  });
  return wrap;
}

/** The 3D picture loads when the map opens, not with the game; without WebGL the buttons still work. */
async function startPicture(canvas: HTMLCanvasElement, places: MapPlaceView[]): Promise<{ filter(filter: Filter): void; resize(): void; dispose(): void } | undefined> {
  try {
    const [{ Engine }, { drawIsland }] = await Promise.all([import("@babylonjs/core/Engines/engine"), import("./islandScene")]);
    if (!canvas.isConnected) return undefined;
    const engine = new Engine(canvas, true, { canvasTabIndex: -1 });
    const island = drawIsland(engine, places);
    const resize = () => island.resize();
    window.addEventListener("resize", resize);
    // The sheet takes its canvas away when it closes.
    const watch = new MutationObserver(() => {
      if (!canvas.isConnected) {
        watch.disconnect();
        window.removeEventListener("resize", resize);
        island.dispose();
      }
    });
    watch.observe(document.body, { childList: true, subtree: true });
    return island;
  } catch {
    return undefined;
  }
}

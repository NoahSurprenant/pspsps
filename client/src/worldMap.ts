import { el } from "./dom";
import type { RoomLink, RoomSnapshot, WorldMapView } from "./types";

export function drawMap(room: RoomSnapshot, travel: (roomId: string) => void): HTMLElement {
  const map = el("div", { className: "world-map" });
  const place = (roomLink: RoomLink): HTMLButtonElement => {
    const { id, name } = roomLink;
    const landmark = el("span", { className: "map-landmark" });
    landmark.setAttribute("aria-hidden", "true");
    const button = el("button", { className: "map-place" }, landmark, el("span", { text: name }));
    button.type = "button";
    button.dataset.place = id;
    if (id === room.roomId) {
      button.setAttribute("aria-current", "location");
      button.setAttribute("aria-label", `${name}, you are here`);
      button.disabled = true;
    } else {
      button.addEventListener("click", () => travel(id));
    }
    return button;
  };

  if (room.worldMap?.rooms.length) {
    drawPlan(map, room.worldMap, place);
  } else {
    // A snapshot from an older pod still has the public destinations.
    map.append(...[{ id: room.roomId, name: room.roomName }, ...room.rooms].map(place));
  }
  return map;
}

function drawPlan(map: HTMLElement, plan: WorldMapView, place: (room: RoomLink) => HTMLElement): void {
  const margin = 2;
  const left = Math.min(...plan.rooms.map((room) => room.x - room.width / 2)) - margin;
  const right = Math.max(...plan.rooms.map((room) => room.x + room.width / 2)) + margin;
  const north = Math.max(...plan.rooms.map((room) => room.z + room.depth / 2)) + margin;
  const south = Math.min(...plan.rooms.map((room) => room.z - room.depth / 2)) - margin;
  const [width, depth] = [right - left, north - south];
  const percent = (part: number, span: number) => `${100 * part / span}%`;
  const position = (element: HTMLElement, x: number, z: number) => {
    element.style.left = percent(x - left, width);
    element.style.top = percent(north - z, depth);
  };
  map.classList.add("map-with-plan");
  map.style.aspectRatio = `${width} / ${depth}`;

  for (const { fromX, fromZ, toX, toZ } of plan.routes) {
    const [dx, dy] = [toX - fromX, fromZ - toZ];
    const path = el("span", { className: "map-route" });
    path.setAttribute("aria-hidden", "true");
    position(path, fromX, fromZ);
    if (Math.hypot(dx, dy) < 0.000001) {
      path.classList.add("map-threshold");
    } else {
      path.style.width = percent(Math.hypot(dx, dy), width);
    }
    path.style.transform = `translateY(-50%) rotate(${Math.atan2(dy, dx)}rad)`;
    map.append(path);
  }

  // The buttons can be bigger than a small footprint, so every destination remains a good touch target.
  const readingOrder = [...plan.rooms].sort((a, b) => b.z - a.z || a.x - b.x);
  for (const room of readingOrder) {
    const footprint = el("span", { className: "map-footprint" });
    footprint.setAttribute("aria-hidden", "true");
    footprint.dataset.room = room.id;
    position(footprint, room.x, room.z);
    footprint.style.width = percent(room.width, width);
    footprint.style.height = percent(room.depth, depth);
    const button = place(room);
    position(button, room.x, room.z);
    button.style.width = `calc(${percent(room.width, width)} - 8px)`;
    map.append(footprint, button);
  }
}

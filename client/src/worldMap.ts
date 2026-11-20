import { el } from "./dom";
import type { RoomSnapshot } from "./types";

// Reading order on the map: shops to the north, garden to the west, trail to the south.
const places = ["shop", "cafe", "garden", "town-square", "riverside"];

export function drawMap(room: RoomSnapshot, travel: (roomId: string) => void): HTMLElement {
  const map = el("div", { className: "world-map" });
  const destinations = new Map(room.rooms.map((place) => [place.id, place.name]));
  destinations.set(room.roomId, room.roomName);

  for (const id of [...places, ...[...destinations.keys()].filter((id) => !places.includes(id))]) {
    const name = destinations.get(id);
    if (name === undefined) {
      continue;
    }
    const landmark = el("span", { className: "map-landmark" });
    landmark.setAttribute("aria-hidden", "true");
    const place = el("button", { className: "map-place" }, landmark, el("span", { text: name }));
    place.type = "button";
    place.dataset.place = id;
    if (id === room.roomId) {
      place.setAttribute("aria-current", "location");
      place.setAttribute("aria-label", `${name}, you are here`);
      place.disabled = true;
    } else {
      place.addEventListener("click", () => travel(id));
    }
    map.append(place);
  }
  return map;
}

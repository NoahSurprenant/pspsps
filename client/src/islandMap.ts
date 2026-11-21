import type { MapPlaceView } from "./types";

export const FILTERS = ["all", "places", "games", "shops", "pets"] as const;
export type Filter = (typeof FILTERS)[number];

export const filterLabel = (filter: Filter): string => (filter === "all" ? "All" : filter.charAt(0).toUpperCase() + filter.slice(1));

export const matches = (place: MapPlaceView, filter: Filter): boolean => filter === "all" || place.category === filter;

/** The island is drawn from a fixed camera: orthographic, from the south, this far above the horizon. */
export const VIEW = { halfWidth: 52, halfHeight: 39, elevation: (52 * Math.PI) / 180 };

/** Where a point of the island lands on the picture, in percent from its top left; height is metres above the ground. */
export function screenPoint(x: number, z: number, height = 0): { left: number; top: number } {
  const up = height * Math.cos(VIEW.elevation) + z * Math.sin(VIEW.elevation);
  return {
    left: (100 * (x + VIEW.halfWidth)) / (2 * VIEW.halfWidth),
    top: (100 * (VIEW.halfHeight - up)) / (2 * VIEW.halfHeight),
  };
}

export const isOpen = (place: MapPlaceView): boolean => place.roomId !== null;

export function placeName(place: MapPlaceView, here: string): string {
  if (place.roomId === here) return `${place.name}, you are here`;
  return isOpen(place) ? place.name : `${place.name}, coming soon`;
}

/** A mouse or the keyboard goes straight there; a finger first shows what it touched, so the second tap is a choice. */
export const shouldTravel = (alreadySelected: boolean, pointerType: string): boolean =>
  pointerType !== "touch" || alreadySelected;

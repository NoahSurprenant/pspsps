import type { FurColor } from "./types";

/** A coat: the main colour, and the colour of the ears, paws and chest. */
export interface Coat {
  body: string;
  accent: string;
}

const coats: Record<FurColor, Coat> = {
  Orange: { body: "#f0a04b", accent: "#d9822b" },
  Black: { body: "#2b2b33", accent: "#1c1c22" },
  White: { body: "#f6f3ee", accent: "#e5ddd2" },
  Grey: { body: "#9aa0a8", accent: "#7c828b" },
  Cream: { body: "#f3dfb8", accent: "#e6c995" },
  Brown: { body: "#8a5a3b", accent: "#6e4329" },
  Calico: { body: "#f6f3ee", accent: "#e08a3c" },
  Tuxedo: { body: "#2b2b33", accent: "#f6f3ee" },
};

export function coatOf(fur: FurColor): Coat {
  return coats[fur] ?? coats.Grey;
}

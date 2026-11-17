import type { Emote, Pose } from "./types";

/** The emote bar, in order: what each button says and the number key that does it. */
export const emoteBar: readonly { emote: Emote; label: string; key: string }[] = [
  { emote: "sit", label: "Sit", key: "1" },
  { emote: "meow", label: "Meow", key: "2" },
  { emote: "wave", label: "Wave", key: "3" },
  { emote: "sleep", label: "Sleep", key: "4" },
];

/** How each held pose squashes the cat's body (x, y, z scale). */
export const poseScales: Record<Pose, readonly [number, number, number]> = {
  // Sitting: shorter and a little rounder.
  sit: [1.1, 0.7, 0.9],
  // Asleep: lying flat, spread out.
  sleep: [1.25, 0.4, 1.2],
};

/** The emote a key press asks for: 1-4 with no modifiers, and never while the player is typing. */
export function emoteForKey(event: Pick<KeyboardEvent, "key" | "altKey" | "ctrlKey" | "metaKey" | "repeat" | "target">): Emote | undefined {
  if (event.altKey || event.ctrlKey || event.metaKey || event.repeat || isTyping(event.target)) {
    return undefined;
  }
  return emoteBar.find((entry) => entry.key === event.key)?.emote;
}

/** Whether keys pressed here are typing: text inputs, text areas and editable elements. */
export function isTyping(target: EventTarget | null): boolean {
  const element = target as Partial<Pick<HTMLElement, "tagName" | "isContentEditable">> | null;
  return element?.isContentEditable === true || ["INPUT", "TEXTAREA", "SELECT"].includes(element?.tagName ?? "");
}

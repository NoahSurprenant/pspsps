import type { Emote, Pose } from "./types";

/** The emote bar, in order: what each button says and the number key that does it. */
export const emoteBar: readonly { emote: Emote; label: string; key: string }[] = [
  { emote: "sit", label: "Sit", key: "1" },
  { emote: "meow", label: "Meow", key: "2" },
  { emote: "wave", label: "Wave", key: "3" },
  { emote: "sleep", label: "Sleep", key: "4" },
];

/** The emotes a cat holds until it walks, each an animation of the cat (cat.ts). */
export const poses: readonly Pose[] = ["sit", "sleep"];

/** Whether <emote> is one the cat holds until it walks (sit, sleep). */
export function isPose(emote: Emote): emote is Pose {
  return (poses as readonly Emote[]).includes(emote);
}

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

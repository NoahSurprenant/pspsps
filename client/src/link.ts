import type { Note } from "./ui";

// "Save your cat": a guest links a Discord or Google login to the cat they already have (/auth/link/{provider}).

/** Where the "Save your cat with ..." button goes. */
export function linkUrl(provider: string): string {
  return `/auth/link/${encodeURIComponent(provider)}`;
}

/** What to tell the player after the server sent them back from linking (?link=...), if it did. */
export function linkMessage(search: string): Note | undefined {
  switch (new URLSearchParams(search).get("link")) {
    case "linked":
      return { text: "Your cat is saved. Sign in with that account from anywhere to play it.", error: false };
    case "taken":
      return {
        text: "That account already has its own cat, so this one wasn't linked. You're still playing this cat.",
        error: true,
      };
    case "failed":
      return { text: "Saving your cat didn't work. Try again.", error: true };
    default:
      return undefined;
  }
}

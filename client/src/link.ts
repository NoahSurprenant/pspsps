import type { Note } from "./ui";

// "Save your cat": a guest links a Discord or Google login to the cat they already have (/auth/link/{provider}).

/** Where "Sign in with ..." goes. */
export function loginUrl(provider: string): string {
  return `/auth/login/${encodeURIComponent(provider)}`;
}

/** Where the "Save your cat with ..." button posts (a form post from this page: the server refuses other sites). */
export function linkUrl(provider: string): string {
  return `/auth/link/${encodeURIComponent(provider)}`;
}

/** The ?link= results the server sends (src/Pspsps.Server/Auth/AuthRedirects.cs): change them together. */
export const LINK_RESULTS = ["linked", "taken", "refused", "failed"] as const;
export type LinkResult = (typeof LINK_RESULTS)[number];

/**
 * What to tell the player after the server sent them back from saving their cat (?link=...) or signing in
 * (?signin=failed), if it did. A signed-out player who was saving their cat has lost it: their guest cookie is gone.
 */
export function addressNote(search: string, signedIn: boolean): Note | undefined {
  const query = new URLSearchParams(search);
  if (query.get("signin") === "failed") {
    return { text: "Signing in didn't work. Try again.", error: true };
  }

  switch (query.get("link")) {
    case "linked":
      return { text: "Your cat is saved. Sign in with that account from anywhere to play it.", error: false };
    case "taken":
      return {
        text: "That account already has its own cat, so this one wasn't linked. You're still playing this cat.",
        error: true,
      };
    case "refused":
      return {
        text: "This cat already has a login from that provider, so it can't take another one from there.",
        error: true,
      };
    case "failed":
      return signedIn
        ? { text: "Saving your cat didn't work. Try again.", error: true }
        : { text: "Your guest cat couldn't be found. Sign in or play as a new guest.", error: true };
    default:
      return undefined;
  }
}

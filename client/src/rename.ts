// The player's own name, shown above their cat and renamed from their own card (DisplayNameRules.cs). The length is
// enforced here only to size the box and give an early hint; the server trims, collapses spaces, checks the configured
// chat filter and a short list of staff words, and checks the rename hasn't already happened once in the last day.

export const minDisplayNameLength = 3;
export const maxDisplayNameLength = 20;

/** What the player's own card offers to rename their cat; built by main.ts, drawn by ui.ts. */
export interface OwnCardRename {
  /** Whether the inline text box is open now (instead of the small "Rename" button). */
  editing: boolean;
  /** Opens the text box. */
  start: () => void;
  /** Closes it without saving. */
  cancel: () => void;
  /** Saves the typed name. */
  save: (name: string) => void;
}

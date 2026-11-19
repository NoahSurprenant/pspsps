// The player's own name, shown above their cat and renamed from their own card (DisplayNameRules.cs). The length is
// capped here only to size the box; a minimum isn't, since the browser's own validation would block the Save button's
// submit before the form's own listener ran. The server decides for real: a name too short, filtered, or renamed
// again too soon comes back as a toast, like any other refusal.

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

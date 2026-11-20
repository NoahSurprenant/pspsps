import { el } from "./dom";

export interface RoomSummary { roomId: string; name: string; published: number | null; draft: number | null }
export interface VersionSummary { number: number; state: string; createdAt: string; publishedAt: string | null }
export interface RoomDetail {
  roomId: string;
  versions: VersionSummary[];
  published: unknown | null;
  draft: unknown | null;
  overlays: { id: string }[];
}

/** One line for a room in the list: its name, the version players have and whether a draft waits. */
export function roomLine(room: RoomSummary): string {
  const live = room.published === null ? "not published" : `version ${room.published} live`;
  return room.draft === null ? `${room.name} (${live})` : `${room.name} (${live}, draft ${room.draft})`;
}

/** The layout or overlay as text for editing; null when it isn't valid JSON. */
export function parseJson(text: string): unknown | null {
  try {
    return JSON.parse(text) as unknown;
  } catch {
    return null;
  }
}

/** The text of what the server said was wrong with a draft, or that it's ready. */
export function problemsText(problems: string[]): string {
  return problems.length === 0 ? "Saved. It can be published." : `Saved, but it can't be published yet: ${problems.join(" ")}`;
}

async function send(method: string, url: string, body?: unknown): Promise<unknown> {
  const response = await fetch(url, {
    method,
    headers: body === undefined ? {} : { "Content-Type": "application/json" },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (!response.ok) {
    const refusal = (await response.json().catch(() => null)) as { message?: string } | null;
    throw new Error(refusal?.message ?? `That didn't work (${response.status}).`);
  }
  return response.status === 204 ? null : response.json().catch(() => null);
}

function button(text: string, onClick: () => void): HTMLButtonElement {
  const element = el("button", { text });
  element.type = "button";
  element.addEventListener("click", onClick);
  return element;
}

function textArea(rows: number, label: string): HTMLTextAreaElement {
  const area = el("textarea");
  area.rows = rows;
  area.spellcheck = false;
  area.setAttribute("aria-label", label);
  area.style.width = "100%";
  area.style.fontFamily = "monospace";
  return area;
}

/**
 * The rooms section of the admin page: every room with its versions; a room's layout as JSON to edit (this is also the
 * import and export), saved as a draft, checked and published, or an old version rolled back to; and its overlays.
 */
export function roomsSection(say: (message: string) => void): HTMLElement {
  const list = el("ul", { className: "list" });
  const editor = el("div", {});
  const body = el("section", {}, el("h2", { text: "Rooms" }),
    el("p", { className: "quiet", text: "A room is what its published version says. Saving makes a draft; publishing checks every room together first." }),
    list, editor);

  const run = async (action: () => Promise<void>): Promise<void> => {
    try {
      await action();
    } catch (error) {
      say(error instanceof Error ? error.message : String(error));
    }
  };

  const base = (roomId: string): string => `/api/admin/rooms/${encodeURIComponent(roomId)}`;

  async function loadList(): Promise<void> {
    const rooms = (await (await fetch("/api/admin/rooms")).json()) as RoomSummary[];
    const add = el("input");
    add.setAttribute("aria-label", "New room ID");
    add.placeholder = "new-room-id";
    list.replaceChildren(
      ...rooms.map((room) => el("li", {}, button(roomLine(room), () => void run(() => open(room.roomId))))),
      el("li", {}, add, button("New room", () => { if (add.value) void run(() => open(add.value, true)); })));
  }

  async function open(roomId: string, isNew = false): Promise<void> {
    const detail = isNew ? null : ((await (await fetch(base(roomId))).json()) as RoomDetail);
    const layout = textArea(24, `${roomId} layout as JSON`);
    layout.value = JSON.stringify(detail?.draft ?? detail?.published ?? {}, null, 2);
    const overlay = textArea(10, "Overlay as JSON");
    const refresh = async (): Promise<void> => { await loadList(); await open(roomId); };

    editor.replaceChildren(
      el("h3", { text: `${roomId}${detail?.draft ? " (editing the draft)" : ""}` }),
      layout,
      el("div", {},
        button("Save draft", () => void run(async () => {
          const parsed = parseJson(layout.value);
          if (parsed === null) { say("That isn't valid JSON."); return; }
          const saved = (await send("PUT", `${base(roomId)}/draft`, parsed)) as { problems: string[] } | null;
          say(problemsText(saved?.problems ?? []));
          await refresh();
        })),
        button("Publish draft", () => void run(async () => {
          await send("POST", `${base(roomId)}/publish`);
          say(`${roomId} is published.`);
          await refresh();
        })),
        button("Discard draft", () => void run(async () => {
          await send("DELETE", `${base(roomId)}/draft`);
          say("The draft is gone.");
          await refresh();
        }))),
      el("h3", { text: "Versions" }),
      el("ul", { className: "list" }, ...(detail?.versions ?? []).map((version) =>
        el("li", { text: `Version ${version.number}: ${version.state.toLowerCase()}, made ${version.createdAt} ` },
          ...(version.state === "Archived"
            ? [button("Roll back to this", () => void run(async () => {
              await send("POST", `${base(roomId)}/rollback/${version.number}`);
              say(`${roomId} is back to version ${version.number}.`);
              await refresh();
            }))]
            : [])))),
      el("h3", { text: "Seasonal overlays" }),
      el("ul", { className: "list" }, ...(detail?.overlays ?? []).map((existing) =>
        el("li", { text: `${existing.id} ` },
          button("Edit", () => { overlay.value = JSON.stringify(existing, null, 2); }),
          button("Delete", () => void run(async () => {
            await send("DELETE", `${base(roomId)}/overlays/${encodeURIComponent(existing.id)}`);
            say(`${existing.id} is gone.`);
            await refresh();
          }))))),
      overlay,
      button("Save overlay", () => void run(async () => {
        const parsed = parseJson(overlay.value) as { id?: string } | null;
        if (parsed === null || !parsed.id) { say("An overlay is JSON with an id."); return; }
        await send("PUT", `${base(roomId)}/overlays/${encodeURIComponent(parsed.id)}`, parsed);
        say(`${parsed.id} is saved.`);
        await refresh();
      })));
  }

  void run(loadList);
  return body;
}

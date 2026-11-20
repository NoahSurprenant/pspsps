import { el } from "./dom";
import { calendar, fieldsOf, monthNames, summary, windowOf, windowProblem, type WindowFields, type WindowJson } from "./overlayWindow";
import { type CanvasLayout, isLayout, newRoomTemplate, roomCanvas } from "./roomCanvas";

export interface LookOptions {
  propKinds: string[];
  floors: string[];
  walls: string[];
  grounds: string[];
  generators: string[];
  wildlife: string[];
}

export interface LooksData {
  rooms: { id: string; look: unknown }[];
  variants: { id: string; sky: string; wallColour: string }[];
}

/** The names a room look may use, one line each, for the editor to show beside the JSON. */
export function optionsText(options: LookOptions): string {
  return [
    `Prop kinds: ${options.propKinds.join(", ")}`,
    `Floor painters: ${options.floors.join(", ")}`,
    `Wall painters: ${options.walls.join(", ")}`,
    `Ground painters: ${options.grounds.join(", ")}`,
    `Backdrop generators: ${options.generators.join(", ")}`,
    `Wildlife: ${options.wildlife.join(", ")}`,
  ].join("\n");
}

/** The query that publishes a room together with others: new rooms need the rooms whose doors lead to them. */
export function publishQuery(together: string): string {
  const ids = together.split(/[\s,]+/).filter((id) => id.length > 0);
  return ids.length === 0 ? "" : `?${ids.map((id) => `with=${encodeURIComponent(id)}`).join("&")}`;
}

/** A starting look for a new room, to edit: plain colours and a plain floor. */
export function newLookTemplate(): unknown {
  return { sky: "#cfe8ff", floorColour: "#c8b79a", wallColour: "#e6dccb", floor: { painter: "planks", colour: "#b08a5a" }, wall: { painter: "panelled" }, backdrop: { parts: [] } };
}

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
 * When an overlay is on, picked with dates instead of typed: two date boxes, a yearly box, what it says in words, and a
 * strip of the year showing the days it is on. It writes the window into the overlay's JSON, which keeps the rest.
 */
function windowPicker(overlay: HTMLTextAreaElement): { element: HTMLElement; load: () => void } {
  const start = el("input");
  const end = el("input");
  start.type = "date";
  end.type = "date";
  start.setAttribute("aria-label", "Overlay starts");
  end.setAttribute("aria-label", "Overlay ends (this day is off)");
  const yearly = el("input");
  yearly.type = "checkbox";
  yearly.setAttribute("aria-label", "Repeats every year");
  const words = el("p", { className: "quiet" });
  const strip = el("div", {});
  const fields = (): WindowFields => ({ start: start.value, end: end.value, yearly: yearly.checked });

  const show = (): void => {
    const now = fields();
    const problem = windowProblem(now);
    words.textContent = problem ?? summary(now);
    strip.replaceChildren(...(problem === null ? calendar(now, Number(now.start.slice(0, 4)) || new Date().getUTCFullYear()).map((days, month) =>
      el("span", { text: `${monthNames[month] ?? ""} ${days.map((on) => (on ? "#" : ".")).join("")}`, className: "quiet" })) : []));
    strip.style.fontFamily = "monospace";
    strip.style.display = "grid";
  };
  const write = (): void => {
    show();
    const parsed = parseJson(overlay.value) as Record<string, unknown> | null;
    if (parsed === null || windowProblem(fields()) !== null) return;
    overlay.value = JSON.stringify({ ...parsed, window: windowOf(fields()) }, null, 2);
  };
  const load = (): void => {
    const window = (parseJson(overlay.value) as { window?: WindowJson } | null)?.window;
    if (window) {
      const now = fieldsOf(window);
      start.value = now.start;
      end.value = now.end;
      yearly.checked = now.yearly;
    }
    show();
  };
  for (const input of [start, end, yearly]) input.addEventListener("change", write);
  return { element: el("div", {}, el("label", { text: "From " }, start), el("label", { text: " until " }, end), el("label", { text: " every year " }, yearly), words, strip), load };
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
    add.style.minHeight = "44px";
    add.style.fontSize = "16px";
    list.replaceChildren(
      ...rooms.map((room) => el("li", {}, button(roomLine(room), () => void run(() => open(room.roomId))))),
      el("li", {}, add, button("New room", () => { if (add.value) void run(() => open(add.value, true)); })));
  }

  async function open(roomId: string, isNew = false): Promise<void> {
    const detail = isNew ? null : ((await (await fetch(base(roomId))).json()) as RoomDetail);
    const options = (await (await fetch("/api/admin/looks/options")).json().catch(() => null)) as LookOptions | null;
    const looks = (await (await fetch("/api/looks")).json().catch(() => null)) as LooksData | null;
    const layout = textArea(24, `${roomId} layout as JSON`);
    layout.value = JSON.stringify(detail?.draft ?? detail?.published ?? newRoomTemplate(roomId), null, 2);
    const overlay = textArea(10, "Overlay as JSON");
    const picker = windowPicker(overlay);
    const refresh = async (): Promise<void> => { await loadList(); await open(roomId); };

    const together = el("input");
    together.setAttribute("aria-label", "Publish together with these rooms");
    together.placeholder = "publish with: room-a, room-b";
    const picture = el("div", {});
    const showPicture = (): void => {
      const parsed = parseJson(layout.value);
      if (!isLayout(parsed)) { picture.replaceChildren(el("p", { className: "quiet", text: "The picture needs a layout with a floor size, props and doors." })); return; }
      picture.replaceChildren(roomCanvas(parsed, options?.propKinds ?? [], (changed: CanvasLayout) => { layout.value = JSON.stringify(changed, null, 2); }));
    };
    layout.addEventListener("change", showPicture);
    showPicture();
    const lookText = textArea(12, `${roomId} look as JSON`);
    lookText.value = JSON.stringify(looks?.rooms.find((entry) => entry.id === roomId)?.look ?? newLookTemplate(), null, 2);
    const variantText = textArea(4, "Look variants as JSON: name, sky, wallColour");
    variantText.value = JSON.stringify(looks?.variants ?? [], null, 2);

    editor.replaceChildren(
      el("h3", { text: `${roomId}${detail?.draft ? " (editing the draft)" : ""}` }),
      picture,
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
          await send("POST", `${base(roomId)}/publish${publishQuery(together.value)}`);
          say(`${roomId} is published.`);
          await refresh();
        })),
        together,
        button("Discard draft", () => void run(async () => {
          await send("DELETE", `${base(roomId)}/draft`);
          say("The draft is gone.");
          await refresh();
        }))),
      el("h3", { text: "Look" }),
      el("p", { className: "quiet", text: "How the browser paints this room. A room needs a look before it can be published." }),
      lookText,
      button("Save look", () => void run(async () => {
        const parsed = parseJson(lookText.value);
        if (parsed === null) { say("That isn't valid JSON."); return; }
        await send("PUT", `/api/admin/looks/rooms/${encodeURIComponent(roomId)}`, parsed);
        say(`The look of ${roomId} is saved.`);
        await refresh();
      })),
      el("pre", { className: "quiet", text: options ? optionsText(options) : "" }),
      el("h3", { text: "Look variants (for overlays)" }),
      variantText,
      button("Save variants", () => void run(async () => {
        const parsed = parseJson(variantText.value) as { id: string; sky: string; wallColour: string }[] | null;
        if (!Array.isArray(parsed)) { say("Variants are a JSON list."); return; }
        for (const variant of parsed) {
          await send("PUT", `/api/admin/looks/variants/${encodeURIComponent(variant.id)}`, { sky: variant.sky, wallColour: variant.wallColour });
        }
        say("The variants are saved.");
        await refresh();
      })),
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
          button("Edit", () => { overlay.value = JSON.stringify(existing, null, 2); picker.load(); }),
          button("Delete", () => void run(async () => {
            await send("DELETE", `${base(roomId)}/overlays/${encodeURIComponent(existing.id)}`);
            say(`${existing.id} is gone.`);
            await refresh();
          }))))),
      picker.element,
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

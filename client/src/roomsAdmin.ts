import { el } from "./dom";
import { roomPreview } from "./roomPreview";
import { calendar, fieldsOf, monthNames, summary, windowOf, windowProblem, type WindowFields, type WindowJson } from "./overlayWindow";
import { type CanvasDoor, type CanvasLayout, isLayout, newRoomTemplate, roomCanvas } from "./roomCanvas";

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
  painters?: { id: string; surface: string; steps: unknown[] }[];
  wildlife?: { id: string; parts: unknown[]; motion: unknown }[];
}

/** The body a recipe is saved with (its name is in the address), from the entry the list shows. */
export function recipeBody(kind: "painters" | "wildlife", entry: Record<string, unknown>): Record<string, unknown> {
  return kind === "painters" ? { surface: entry.surface, steps: entry.steps } : { parts: entry.parts, motion: entry.motion };
}

export interface ImportReport { applied: string[]; unchanged: string[]; problems: string[] }

/** What loading the repository's content file did, in words for the admin. */
export function importText(report: ImportReport | null): string {
  if (report === null) return "Nothing came back.";
  const lines = [...report.applied, ...report.problems.map((problem) => `Not applied: ${problem}`)];
  return lines.length > 0 ? lines.join("\n") : `Nothing to load: ${report.unchanged.length} items already match.`;
}

/** Where the published content is downloaded as JSON, to commit to the repository for review. */
export const exportUrl = "/api/admin/export";

function exportLink(): HTMLElement {
  const link = el("a", { text: "Download everything as JSON (rooms, looks, recipes, prop designs)" });
  link.href = exportUrl;
  link.download = "content.json";
  return el("p", {}, link);
}

export function newPainterTemplate(): unknown {
  return { id: "stripes", surface: "floor", steps: [{ op: "fill", colour: "main" }, { op: "rect", colour: "accent", x: 0, y: 0, width: 0.5, height: 100, repeat: { dx: 1, dy: 0 } }] };
}

export function newWildlifeTemplate(): unknown {
  return { id: "moth", parts: [{ shape: "ball", diameter: 0.2, height: 0.05, at: [0, 0, 0], colour: "#d9d2c3" }], motion: { kind: "wander", speed: 0.6, height: 1.2, reach: 3 } };
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
export function publishQuery(together: string, goLiveAt?: string): string {
  const ids = together.split(/[\s,]+/).filter((id) => id.length > 0);
  const parts = [...ids.map((id) => `with=${encodeURIComponent(id)}`), ...(goLiveAt ? [`at=${encodeURIComponent(goLiveAt)}`] : [])];
  return parts.length === 0 ? "" : `?${parts.join("&")}`;
}

/** The time a date-time field holds (the admin's local time) as the instant the server is told; null when it is empty or not a time. */
export function goLiveInstant(local: string): string | null {
  const time = local === "" ? Number.NaN : new Date(local).getTime();
  return Number.isNaN(time) ? null : new Date(time).toISOString();
}

/** Some of the drafts waiting, published together: the first is the room the request is for, the rest ride along. */
export function draftSetUrl(ids: string[], goLiveAt?: string): { roomId: string; query: string } | null {
  const [roomId, ...rest] = ids;
  return roomId === undefined ? null : { roomId, query: publishQuery(rest.join(","), goLiveAt) };
}

/** The query that saves over the draft as it was opened, so a draft someone else saved meanwhile is refused; none for a new draft. */
export function draftQuery(revision: number | null | undefined): string {
  return revision === null || revision === undefined ? "" : `?revision=${revision}`;
}

export interface AuditLine { at: string; adminId: string | null; action: string; target: string; detail: string }

/** One line of what admins did: when, what, to which room and the detail. */
export function auditText(line: AuditLine): string {
  const detail = line.detail === "" ? "" : ` (${line.detail})`;
  return `${line.at.slice(0, 16).replace("T", " ")} ${line.action} ${line.target}${detail}`;
}

/** A starting look for a new room, to edit: plain colours and a plain floor. */
export function newLookTemplate(): unknown {
  return { sky: "#cfe8ff", floorColour: "#c8b79a", wallColour: "#e6dccb", floor: { painter: "planks", colour: "#b08a5a" }, wall: { painter: "panelled" }, backdrop: { parts: [] } };
}

export interface RoomSummary { roomId: string; name: string; published: number | null; draft: number | null; goLiveAt?: string | null }
export interface VersionSummary { number: number; state: string; createdAt: string; publishedAt: string | null }
export interface RoomDetail {
  roomId: string;
  versions: VersionSummary[];
  published: unknown | null;
  draft: unknown | null;
  overlays: { id: string }[];
  /** Overlays of this room in the reviewed content file that are not saved yet (they wait for the room to be published); previewable. */
  draftRevision?: number | null;
  goLiveAt?: string | null;
}

/** When a draft goes live by itself, as the admin reads it; empty for a draft that waits to be published by hand. */
export function goLiveText(goLiveAt: string | null | undefined): string {
  return goLiveAt ? `goes live ${new Date(goLiveAt).toLocaleString()}` : "";
}

/** One line for a room in the list: its name, the version players have and whether a draft waits. */
export function roomLine(room: RoomSummary): string {
  const live = room.published === null ? "not published" : `version ${room.published} live`;
  if (room.draft === null) return `${room.name} (${live})`;
  const when = goLiveText(room.goLiveAt);
  const goLive = when === "" ? "" : `, ${when}`;
  return `${room.name} (${live}, draft ${room.draft}${goLive})`;
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
  const waiting = el("div", {});
  const history = el("ul", { className: "list" });
  const loadHistory = async (): Promise<void> => {
    const lines = (await (await fetch("/api/admin/rooms/audit?limit=50")).json()) as AuditLine[];
    history.replaceChildren(...lines.map((line) => el("li", { text: auditText(line) })));
  };
  const body = el("section", {}, el("h2", { text: "Rooms" }),
    el("p", { className: "quiet", text: "A room is what its published version says. Saving makes a draft; publishing checks every room together first." }),
    list, waiting, editor, exportLink(), button("Load the reviewed content as drafts", () => void run(async () => {
      const report = (await send("POST", "/api/admin/import")) as ImportReport | null;
      say(importText(report));
      await loadList();
    })), el("h3", { text: "What admins changed" }), button("Show the latest changes", () => void run(loadHistory)), history);

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
    showWaiting(rooms.filter((room) => room.draft !== null));
    list.replaceChildren(
      ...rooms.map((room) => el("li", {}, button(roomLine(room), () => void run(() => open(room.roomId))))),
      el("li", {}, add, button("New room", () => { if (add.value) void run(() => open(add.value, true)); })));
  }

  function showWaiting(drafts: RoomSummary[]): void {
    if (drafts.length === 0) { waiting.replaceChildren(); return; }
    const boxes = drafts.map((room) => {
      const box = el("input");
      box.type = "checkbox";
      box.setAttribute("aria-label", `Publish ${room.roomId}`);
      return { room, box };
    });
    const at = el("input");
    at.type = "datetime-local";
    at.setAttribute("aria-label", "Go live at (your time)");
    const ticked = (): string[] => boxes.filter(({ box }) => box.checked).map(({ room }) => room.roomId);
    const publish = (later: boolean): void => void run(async () => {
      const when = later ? goLiveInstant(at.value) : undefined;
      if (later && when === null) { say("Pick the time to go live at."); return; }
      const set = draftSetUrl(ticked(), when ?? undefined);
      if (set === null) { say("Tick the drafts to publish."); return; }
      await send("POST", `${base(set.roomId)}/publish${set.query}`);
      say(later ? `${ticked().join(", ")} will go live at ${at.value}.` : `${ticked().join(", ")} published together.`);
      await loadList();
    });
    waiting.replaceChildren(
      el("h3", { text: "Drafts waiting" }),
      el("p", { className: "quiet", text: "Tick some drafts to publish just those together (they are checked as a set), now or at a time." }),
      ...boxes.map(({ room, box }) => el("label", {}, box, el("span", { text: ` ${roomLine(room)} ` }))),
      button("Publish ticked", () => publish(false)),
      at,
      button("Publish ticked at that time", () => publish(true)));
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
    const goLive = el("input");
    goLive.type = "datetime-local";
    goLive.setAttribute("aria-label", "Go live at (your time)");
    const scheduleControls = (): HTMLElement[] => [
      goLive,
      button("Publish at that time", () => void run(async () => {
        const when = goLiveInstant(goLive.value);
        if (when === null) { say("Pick the time to go live at."); return; }
        await send("POST", `${base(roomId)}/publish${publishQuery(together.value, when)}`);
        say(`${roomId} will go live at ${goLive.value}.`);
        await refresh();
      })),
      ...(detail?.goLiveAt
        ? [el("span", { className: "quiet", text: ` ${goLiveText(detail.goLiveAt)} ` }), button("Take off the schedule", () => void run(async () => {
          await send("DELETE", `${base(roomId)}/schedule`);
          say("It will wait to be published by hand.");
          await refresh();
        }))]
        : []),
    ];
    const stage = el("canvas", {});
    stage.width = 640;
    stage.height = 400;
    stage.style.maxWidth = "100%";
    stage.setAttribute("aria-label", `Preview of ${roomId} in 3D`);
    stage.hidden = true;
    const preview = roomPreview(stage);
    const look = (overlayId?: string): void => void run(async () => {
      await preview.show(roomId, overlayId);
      stage.hidden = false;
      say(overlayId ? `${roomId} with ${overlayId} on, with every draft in place.` : `${roomId} with every draft in place.`);
    });
    const roomIds = ((await (await fetch("/api/admin/rooms")).json().catch(() => [])) as RoomSummary[]).map((room) => room.roomId);
    const addDoorPair = async (door: CanvasDoor, back: CanvasDoor): Promise<void> => {
      const other = (await (await fetch(base(door.toRoom))).json()) as RoomDetail;
      const otherLayout = (other.draft ?? other.published) as CanvasLayout | null;
      if (!isLayout(otherLayout)) throw new Error(`${door.toRoom} has no layout to add the way back to.`);
      await send("PUT", `${base(door.toRoom)}/draft`, { ...otherLayout, doors: [...otherLayout.doors, back] });
      say(`The way back is in ${door.toRoom}'s draft: publish both rooms together.`);
    };
    const showPicture = (): void => {
      const parsed = parseJson(layout.value);
      if (!isLayout(parsed)) { picture.replaceChildren(el("p", { className: "quiet", text: "The picture needs a layout with a floor size, props and doors." })); return; }
      picture.replaceChildren(roomCanvas(parsed, options?.propKinds ?? [], (changed: CanvasLayout) => { layout.value = JSON.stringify(changed, null, 2); }, { addDoorPair, rooms: roomIds, roomId }));
    };
    layout.addEventListener("change", showPicture);
    showPicture();
    const lookText = textArea(12, `${roomId} look as JSON`);
    lookText.value = JSON.stringify(looks?.rooms.find((entry) => entry.id === roomId)?.look ?? newLookTemplate(), null, 2);
    const variantText = textArea(4, "Look variants as JSON: name, sky, wallColour");
    variantText.value = JSON.stringify(looks?.variants ?? [], null, 2);

    const deleteRecipe = (kind: "painters" | "wildlife", id: string): HTMLElement => button("Delete", () => void run(async () => {
      await send("DELETE", `/api/admin/looks/${kind}/${encodeURIComponent(id)}`);
      say(`${id} is gone.`);
      await refresh();
    }));

    const recipeEditor = (kind: "painters" | "wildlife", title: string, existing: { id: string }[], template: unknown): HTMLElement[] => {
      const text = textArea(10, `${title} as JSON`);
      text.value = JSON.stringify(existing, null, 2);
      return [
        el("h3", { text: title }),
        el("p", { className: "quiet", text: "Named recipes a look can use: a list of entries, each with an id. Starter: " + JSON.stringify(template) }),
        text,
        button(`Save ${title.toLowerCase()}`, () => void run(async () => {
          const parsed = parseJson(text.value) as Record<string, unknown>[] | null;
          if (!Array.isArray(parsed)) { say("Recipes are a JSON list."); return; }
          for (const entry of parsed) {
            await send("PUT", `/api/admin/looks/${kind}/${encodeURIComponent(String(entry.id))}`, recipeBody(kind, entry));
          }
          say(`The ${title.toLowerCase()} are saved.`);
          await refresh();
        })),
        el("ul", { className: "list" }, ...existing.map((entry) => el("li", { text: `${entry.id} ` }, deleteRecipe(kind, entry.id)))),
      ];
    };

    editor.replaceChildren(
      el("h3", { text: `${roomId}${detail?.draft ? " (editing the draft)" : ""}` }),
      picture,
      stage,
      layout,
      el("div", {},
        button("Save draft", () => void run(async () => {
          const parsed = parseJson(layout.value);
          if (parsed === null) { say("That isn't valid JSON."); return; }
          const saved = (await send("PUT", `${base(roomId)}/draft${draftQuery(detail?.draftRevision)}`, parsed)) as { problems: string[] } | null;
          say(problemsText(saved?.problems ?? []));
          await refresh();
        })),
        button("Preview in 3D", () => look()),
        button("Publish draft", () => void run(async () => {
          await send("POST", `${base(roomId)}/publish${publishQuery(together.value)}`);
          say(`${roomId} is published.`);
          await refresh();
        })),
        together,
        ...scheduleControls(),
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
      ...recipeEditor("painters", "Painters", looks?.painters ?? [], newPainterTemplate()),
      ...recipeEditor("wildlife", "Wildlife", looks?.wildlife ?? [], newWildlifeTemplate()),
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
          button("Preview", () => look(existing.id)),
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

import { ChangeDetectionStrategy, Component, computed, inject, signal } from "@angular/core";
import { RouterLink } from "@angular/router";
import { CELL, firstFreePlace, overlapping, pathRects, placedOf, withPlace, type GridPlace, type Placed } from "../../src/worldGrid";
import { messageOf } from "./http";
import { RoomsApi } from "./roomsApi";

interface Kept { layout: Record<string, unknown> & { name: string; width: number; depth: number }; revision: number | null }

/** The outdoor rooms on the world grid: drag a room (or type its cell) to place it; edges that touch line up, shared floor shows red. */
@Component({
  selector: "app-room-map",
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p><a routerLink="/rooms">← All rooms</a></p>
    <h1>World map</h1>
    <p class="quiet">
      Outdoor rooms sit on a grid of {{ cell }} m cells, side by side, so cats can walk from one into the next. A room with no place is indoors and is reached by its doors.
    </p>
    @if (message()) {
      <p class="message" role="status">{{ message() }}</p>
    }
    @if (clashing().length > 0) {
      <p class="message" role="alert">These rooms share floor, which cannot be published: {{ clashing().join(", ") }}.</p>
    }
    <div class="workspace">
      <svg class="world-map" role="img" aria-label="The world map" [attr.viewBox]="viewBox()" (pointermove)="drag($event)" (pointerup)="drop()" (pointerleave)="drop()">
        <rect class="world-grid" [attr.x]="bounds().x" [attr.y]="bounds().z" [attr.width]="bounds().width" [attr.height]="bounds().depth" />
        @for (room of placed(); track room.id) {
          <g (pointerdown)="grab($event, room)">
            @for (path of pathsOf(room); track $index) {
              <rect class="world-path" [attr.x]="path.x" [attr.y]="path.z" [attr.width]="path.width" [attr.height]="path.depth" />
            }
            <rect class="world-room" [class.clash]="clashing().includes(room.id)" [class.chosen]="chosen() === room.id"
              [attr.x]="room.place!.x * cell" [attr.y]="room.place!.z * cell" [attr.width]="room.width" [attr.height]="room.depth" />
            <text class="world-label" [attr.x]="room.place!.x * cell + 0.5" [attr.y]="room.place!.z * cell + 1.5">{{ room.name }}</text>
          </g>
        }
      </svg>
      <div>
        <h2>Rooms</h2>
        <ul class="rooms">
          @for (room of everyRoom(); track room.id) {
            <li class="actions">
              <button type="button" [class.primary]="chosen() === room.id" [attr.aria-pressed]="chosen() === room.id" (click)="chosen.set(room.id)">{{ room.name }} ({{ room.id }})</button>
              @if (room.place) {
                <span class="quiet">cell {{ room.place.x }}, {{ room.place.z }}</span>
              } @else {
                <span class="quiet">indoors</span>
              }
            </li>
          }
        </ul>
        @if (selected(); as room) {
          <div class="group">
            <strong>{{ room.name }}</strong>
            @if (room.place) {
              <label>East <input type="number" step="1" [attr.aria-label]="room.id + ' cell east'" [value]="room.place.x" (change)="moveTo(room.id, 'x', $event)" /></label>
              <label>North <input type="number" step="1" [attr.aria-label]="room.id + ' cell north'" [value]="room.place.z" (change)="moveTo(room.id, 'z', $event)" /></label>
              <button type="button" (click)="setPlace(room.id, null)">Take off the map</button>
            } @else {
              <button type="button" (click)="putOnMap(room)">Put on the map</button>
            }
            @if (!wholeCells(room)) {
              <span class="quiet">Its floor is {{ room.width }} by {{ room.depth }} m: make both a multiple of {{ cell }} to place it.</span>
            }
          </div>
        }
        <div class="actions">
          <button type="button" class="primary" [disabled]="busy() || changed().length === 0" (click)="save()">Save {{ changed().length }} changes as drafts</button>
          <button type="button" [disabled]="busy() || changed().length === 0" (click)="pending.set({})">Undo them</button>
        </div>
        <p class="quiet">Saving puts the new places in each room's draft; publish the rooms together from the list.</p>
      </div>
    </div>
  `,
})
export class RoomMapPage {
  private readonly api = inject(RoomsApi);
  protected readonly cell = CELL;
  private readonly kept = signal<Record<string, Kept>>({});
  protected readonly pending = signal<Record<string, GridPlace | null>>({});
  protected readonly chosen = signal<string | null>(null);
  protected readonly message = signal("");
  protected readonly busy = signal(false);
  private held: { id: string; startX: number; startZ: number; from: GridPlace; scale: number } | null = null;

  protected readonly everyRoom = computed<Placed[]>(() =>
    Object.entries(this.kept()).map(([id, k]) => {
      const room = placedOf(id, k.layout as { name: string; width: number; depth: number; place?: unknown; paths?: unknown });
      const move = this.pending()[id];
      return move === undefined ? room : { ...room, place: move };
    }).sort((a, b) => a.id.localeCompare(b.id)));
  protected readonly placed = computed(() => this.everyRoom().filter((room) => room.place));
  protected readonly clashing = computed(() => [...overlapping(this.everyRoom())].sort());
  protected readonly selected = computed(() => this.everyRoom().find((room) => room.id === this.chosen()) ?? null);
  protected readonly changed = computed(() => Object.keys(this.pending()));
  protected readonly bounds = computed(() => {
    const rooms = this.placed();
    const lefts = rooms.map((r) => (r.place?.x ?? 0) * CELL);
    const tops = rooms.map((r) => (r.place?.z ?? 0) * CELL);
    const strips = rooms.flatMap(pathRects);
    const x = Math.min(0, ...lefts, ...strips.map((s) => s.x)) - 4;
    const z = Math.min(0, ...tops, ...strips.map((s) => s.z)) - 4;
    const right = Math.max(40, ...rooms.map((r) => (r.place?.x ?? 0) * CELL + r.width), ...strips.map((s) => s.x + s.width)) + 4;
    const bottom = Math.max(30, ...rooms.map((r) => (r.place?.z ?? 0) * CELL + r.depth), ...strips.map((s) => s.z + s.depth)) + 4;
    return { x, z, width: right - x, depth: bottom - z };
  });
  protected readonly viewBox = computed(() => { const b = this.bounds(); return `${b.x} ${b.z} ${b.width} ${b.depth}`; });

  constructor() {
    void this.load();
  }

  protected pathsOf = pathRects;

  protected wholeCells = (room: Placed): boolean => room.width % CELL === 0 && room.depth % CELL === 0;

  protected putOnMap(room: Placed): void {
    this.setPlace(room.id, firstFreePlace(this.everyRoom().filter((other) => other.id !== room.id), room.width, room.depth));
  }

  protected setPlace(id: string, place: GridPlace | null): void {
    this.pending.update((all) => ({ ...all, [id]: place }));
  }

  protected moveTo(id: string, axis: "x" | "z", event: Event): void {
    const room = this.everyRoom().find((r) => r.id === id);
    const value = Math.round(Number((event.target as HTMLInputElement).value));
    if (room?.place && Number.isFinite(value)) this.setPlace(id, { ...room.place, [axis]: value });
  }

  protected grab(event: PointerEvent, room: Placed): void {
    if (!room.place) return;
    this.chosen.set(room.id);
    const svg = (event.currentTarget as SVGGElement).ownerSVGElement;
    if (!svg) return;
    this.held = { id: room.id, startX: event.clientX, startZ: event.clientY, from: room.place, scale: svg.getBoundingClientRect().width / this.bounds().width };
  }

  protected drag(event: PointerEvent): void {
    const held = this.held;
    if (!held || held.scale <= 0) return;
    const x = held.from.x + Math.round((event.clientX - held.startX) / held.scale / CELL);
    const z = held.from.z + Math.round((event.clientY - held.startZ) / held.scale / CELL);
    this.setPlace(held.id, { x, z });
  }

  protected drop(): void {
    this.held = null;
  }

  protected async save(): Promise<void> {
    this.busy.set(true);
    try {
      for (const [id, place] of Object.entries(this.pending())) {
        const kept = this.kept()[id];
        if (kept) await this.api.saveDraft(id, withPlace(kept.layout, place), kept.revision);
      }
      const count = this.changed().length;
      this.pending.set({});
      await this.load();
      this.message.set(`Saved ${count} room drafts.`);
    } catch (error) {
      this.message.set(messageOf(error));
    } finally {
      this.busy.set(false);
    }
  }

  private async load(): Promise<void> {
    try {
      const rooms = await this.api.list();
      const details = await Promise.all(rooms.map((room) => this.api.detail(room.roomId)));
      const kept: Record<string, Kept> = {};
      for (const detail of details) {
        const layout = (detail.draft ?? detail.published) as Kept["layout"] | null;
        if (layout) kept[detail.roomId] = { layout, revision: detail.draftRevision ?? null };
      }
      this.kept.set(kept);
    } catch (error) {
      this.message.set(messageOf(error));
    }
  }
}

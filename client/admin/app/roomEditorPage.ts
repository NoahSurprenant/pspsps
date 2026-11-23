import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from "@angular/core";
import { Router, RouterLink } from "@angular/router";
import { type CanvasDoor, type CanvasLayout, isLayout, newRoomTemplate } from "../../src/roomCanvas";
import { goLiveInstant, goLiveText, parseJson, problemsText, type HistoryEntry, type RoomDetail } from "../../src/roomsData";
import { messageOf } from "./http";
import { blankLook } from "../../src/lookForm";
import type { RoomLookData } from "../../src/looks";
import type { LookOptions } from "../../src/roomsData";
import { RoomLookForm } from "./roomLookForm";
import { RoomOverlaysForm } from "./roomOverlaysForm";
import { RoomCanvasHost } from "./roomCanvasHost";
import { RoomPreviewFrame } from "./roomPreviewFrame";
import { RoomsApi } from "./roomsApi";

type Section = "layout" | "look" | "overlays" | "versions";

@Component({
  selector: "app-room-editor",
  imports: [RouterLink, RoomCanvasHost, RoomLookForm, RoomOverlaysForm, RoomPreviewFrame],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p><a routerLink="/rooms">&larr; All rooms</a></p>
    <h1>
      {{ id() }}
      @if (detail()?.draft) {
        <span class="badge draft">editing the draft</span>
      }
    </h1>
    @if (message()) {
      <p class="message" role="status">{{ message() }}</p>
    }
    <div class="editor">
      <nav class="sections" aria-label="Parts of the room">
        <a href="" [class.current]="section() === 'layout'" (click)="show($event, 'layout')">Layout</a>
        <a href="" [class.current]="section() === 'look'" (click)="show($event, 'look')">Look</a>
        <a href="" [class.current]="section() === 'overlays'" (click)="show($event, 'overlays')">Overlays</a>
        <a href="" [class.current]="section() === 'versions'" (click)="show($event, 'versions')">Versions</a>
      </nav>
      <section class="card">
        @if (section() === "overlays") {
          <app-room-overlays [roomId]="id()" [overlays]="detail()?.overlays ?? []" [variants]="variantNames()" (previewing)="previewOverlay.set($event); section.set('layout')" (saved)="reload()" />
        } @else if (section() !== "versions") {
          <h2>{{ section() === "look" ? "Look" : "Layout" }}</h2>
          @if (previewOverlay(); as on) {
            <p class="quiet">Previewing with the overlay {{ on }} on. <button type="button" (click)="previewOverlay.set(undefined)">Show the room without it</button></p>
          }
          <div class="workspace">
            <div>
              @if (section() === "look") {
                @if (look(); as shown) {
                  @if (options(); as names) {
                    <app-room-look-form [look]="shown" [options]="names" (changed)="look.set($event)" />
                  }
                  <div class="actions">
                    <button type="button" class="primary" [disabled]="busy()" (click)="saveLook()">Save look</button>
                    <button type="button" [disabled]="busy() || !detail()?.draftLook" (click)="clearLook()">Take the look out of the draft</button>
                  </div>
                } @else {
                  <p class="quiet">This room has no look yet: it needs one before it can be published.</p>
                  <button type="button" class="primary" (click)="look.set(blank())">Start a look</button>
                }
              } @else {
              @if (layout(); as shown) {
                <app-room-canvas [layout]="shown" [roomId]="id()" [propKinds]="propKinds()" [rooms]="roomIds()" [addWayBack]="wayBack" (changed)="canvasChanged($event)" />
              } @else {
                <p class="quiet">The picture needs a layout with a floor size, props and doors.</p>
              }
              <details>
                <summary>Layout as JSON</summary>
                <textarea rows="18" spellcheck="false" [attr.aria-label]="id() + ' layout as JSON'" [value]="layoutText()" (input)="typed($event)"></textarea>
              </details>
              }
            </div>
            <div>
              <app-room-preview [roomId]="id()" [layout]="parsed()" [look]="look()" [overlay]="previewOverlay()" />
            </div>
          </div>
          <div class="group">
          <strong>Draft</strong>
            <button type="button" class="primary" [disabled]="busy()" (click)="save()">Save draft</button>
            <button type="button" [disabled]="busy() || !detail()?.draft" (click)="publish()">Publish draft</button>
            <button type="button" [disabled]="busy() || !detail()?.draft" (click)="discard()">Discard draft</button>
          </div>
          <div class="group">
            <strong>Publish</strong>
          <label>
            Together with
            <input aria-label="Publish together with these rooms" placeholder="room-a, room-b" [value]="together()" (input)="togetherTyped($event)" />
          </label>
            <input type="datetime-local" aria-label="Go live at (your time)" (input)="goLive.set($any($event.target).value)" />
            <button type="button" [disabled]="busy() || !detail()?.draft" (click)="publishAt()">Publish at that time</button>
            @if (detail()?.goLiveAt) {
              <span class="quiet">{{ liveText() }}</span>
              <button type="button" [disabled]="busy()" (click)="unschedule()">Take off the schedule</button>
            }
          </div>
          <p class="quiet">
            Painters, wildlife and the look variants are in the <a routerLink="/library">Looks library</a>.
          </p>
        } @else {
          <h2>Versions</h2>
          <ul class="rooms">
            @for (version of versions(); track version.number) {
              <li class="actions">
                <span>Version {{ version.number }}: {{ version.state.toLowerCase() }}, made {{ version.createdAt }}</span>
                @if (version.state === "Archived") {
                  <button type="button" [disabled]="busy()" (click)="rollback(version.number)">Roll back to this</button>
                }
              </li>
            } @empty {
              <li class="quiet">No versions yet.</li>
            }
          </ul>
          <h3>Changes saved to the draft</h3>
          <ul class="rooms">
            @for (entry of history(); track entry.id) {
              <li class="actions">
                <span>{{ entry.at }}: {{ entry.action }} {{ entry.detail }}</span>
                <button type="button" (click)="viewed.set(entry)">Show it</button>
                <button type="button" [disabled]="busy()" (click)="restore(entry)">Put it back in the draft</button>
              </li>
            } @empty {
              <li class="quiet">No saved changes yet.</li>
            }
          </ul>
          @if (viewed(); as entry) {
            <p class="quiet">The room as it was saved at {{ entry.at }}:</p>
            <app-room-preview [roomId]="id()" [layout]="entry.layout" [look]="entry.look" />
          }
        }
      </section>
    </div>
  `,
})
export class RoomEditorPage {
  private readonly api = inject(RoomsApi);
  private readonly router = inject(Router);
  readonly id = input.required<string>();
  readonly isNew = input<string | undefined>(undefined, { alias: "new" });
  protected readonly detail = signal<RoomDetail | null>(null);
  protected readonly layoutText = signal("");
  protected readonly together = signal("");
  protected readonly message = signal("");
  protected readonly busy = signal(false);
  protected readonly section = signal<Section>("layout");
  protected readonly versions = computed(() => this.detail()?.versions ?? []);
  protected readonly parsed = computed(() => parseJson(this.layoutText()));
  protected readonly layout = computed(() => { const value = this.parsed(); return isLayout(value) ? value : null; });
  protected readonly propKinds = computed(() => this.options()?.propKinds ?? []);
  protected readonly look = signal<RoomLookData | null>(null);
  protected readonly options = signal<LookOptions | null>(null);
  protected readonly blank = blankLook;
  protected readonly roomIds = signal<string[]>([]);
  protected readonly goLive = signal("");
  protected readonly liveText = computed(() => goLiveText(this.detail()?.goLiveAt));
  protected readonly previewOverlay = signal<string | undefined>(undefined);
  protected readonly variantNames = signal<string[]>([]);
  protected readonly history = signal<HistoryEntry[]>([]);
  protected readonly viewed = signal<HistoryEntry | null>(null);
  protected readonly wayBack = async (door: CanvasDoor, back: CanvasDoor): Promise<void> => {
    await this.api.addWayBack(door, back);
    this.message.set(`The way back is in ${door.toRoom}'s draft: publish both rooms together.`);
  };

  constructor() {
    effect(() => {
      const id = this.id();
      const fresh = this.isNew() !== undefined;
      void this.load(id, fresh);
    });
    void this.api.options().then((options) => this.options.set(options), () => undefined);
    void this.api.looks().then((library) => this.variantNames.set(library.variants.map((variant) => variant.id)), () => undefined);
    void this.api.list().then((rooms) => this.roomIds.set(rooms.map((room) => room.roomId)), () => undefined);
  }

  protected reload(): Promise<void> {
    return this.load(this.id(), false);
  }

  protected show(event: Event, section: Section): void {
    event.preventDefault();
    this.section.set(section);
  }

  protected canvasChanged(layout: CanvasLayout): void {
    this.layoutText.set(JSON.stringify(layout, null, 2));
  }

  protected typed(event: Event): void {
    this.layoutText.set((event.target as HTMLTextAreaElement).value);
  }

  protected togetherTyped(event: Event): void {
    this.together.set((event.target as HTMLInputElement).value);
  }

  protected save(): Promise<void> {
    return this.act(async () => {
      const parsed = parseJson(this.layoutText());
      if (parsed === null) return "That isn't valid JSON.";
      const saved = await this.api.saveDraft(this.id(), parsed, this.detail()?.draftRevision);
      await this.load(this.id(), false);
      if (this.isNew() !== undefined) void this.router.navigate([], { queryParams: { new: null }, replaceUrl: true });
      return problemsText(saved?.problems ?? []);
    });
  }

  protected saveLook(): Promise<void> {
    return this.act(async () => {
      const look = this.look();
      if (!look) return "There is no look to save.";
      await this.api.saveLook(this.id(), look);
      await this.load(this.id(), false);
      return `The look of ${this.id()} is saved in its draft: publish the room to put it live.`;
    });
  }

  protected clearLook(): Promise<void> {
    return this.act(async () => {
      await this.api.clearDraftLook(this.id());
      await this.load(this.id(), false);
      return `The draft of ${this.id()} goes live with the look the room has now.`;
    });
  }

  protected publish(): Promise<void> {
    return this.act(async () => {
      await this.api.publish(this.id(), this.together());
      await this.load(this.id(), false);
      return `${this.id()} is published.`;
    });
  }

  protected publishAt(): Promise<void> {
    return this.act(async () => {
      const when = goLiveInstant(this.goLive());
      if (when === null) return "Pick the time to go live at.";
      await this.api.publish(this.id(), this.together(), when);
      await this.load(this.id(), false);
      return `${this.id()} will go live at ${this.goLive()}.`;
    });
  }

  protected unschedule(): Promise<void> {
    return this.act(async () => {
      await this.api.takeOffSchedule(this.id());
      await this.load(this.id(), false);
      return "It will wait to be published by hand.";
    });
  }

  protected discard(): Promise<void> {
    return this.act(async () => {
      const neverLive = !this.detail()?.published;
      await this.api.discardDraft(this.id());
      await (neverLive ? this.router.navigate(["/rooms"]) : this.load(this.id(), false));
      return "The draft is gone.";
    });
  }

  protected rollback(version: number): Promise<void> {
    return this.act(async () => {
      await this.api.rollback(this.id(), version);
      await this.load(this.id(), false);
      return `${this.id()} is back to version ${version}, as a draft to publish.`;
    });
  }

  protected restore(entry: HistoryEntry): Promise<void> {
    return this.act(async () => {
      await this.api.saveDraft(this.id(), entry.layout, this.detail()?.draftRevision);
      if (entry.look) {
        await this.api.saveLook(this.id(), entry.look);
      }

      await this.load(this.id(), false);
      this.section.set("layout");
      return `The room as saved at ${entry.at} is the draft again.`;
    });
  }

  private async act(action: () => Promise<string>): Promise<void> {
    this.busy.set(true);
    try {
      this.message.set(await action());
    } catch (error) {
      this.message.set(messageOf(error));
    } finally {
      this.busy.set(false);
    }
  }

  private async load(id: string, fresh: boolean): Promise<void> {
    try {
      const detail = fresh ? null : await this.api.detail(id);
      this.detail.set(detail);
      this.viewed.set(null);
      this.history.set(fresh ? [] : await this.api.history(id).catch(() => []));
      this.layoutText.set(JSON.stringify(detail?.draft ?? detail?.published ?? newRoomTemplate(id), null, 2));
      this.look.set((detail?.draftLook ?? (fresh ? null : await this.api.liveLook(id))) as RoomLookData | null);
    } catch (error) {
      this.message.set(messageOf(error));
    }
  }
}

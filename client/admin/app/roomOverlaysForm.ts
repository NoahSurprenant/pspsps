import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from "@angular/core";
import { blankOverlay, propsFrom, withLook, withWindow, type OverlayData } from "../../src/overlayData";
import { calendar, fieldsOf, monthNames, summary, windowProblem, type WindowFields } from "../../src/overlayWindow";
import { messageOf } from "./http";
import { RoomsApi } from "./roomsApi";

/** A room's seasonal overlays: when each is on (date boxes and a strip of the year), what it adds and removes, its look. */
@Component({
  selector: "app-room-overlays",
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2>Overlays</h2>
    <p class="quiet">Seasonal changes to the room: props that come and go, and a look to paint it in, between two dates.</p>
    @if (message()) {
      <p class="message" role="status">{{ message() }}</p>
    }
    <ul class="rooms">
      @for (overlay of overlays(); track overlay.id) {
        <li class="actions">
          <span>{{ overlay.name }} ({{ overlay.id }})</span>
          <button type="button" (click)="edit(overlay)">Edit</button>
          <button type="button" (click)="previewing.emit(overlay.id)">Preview</button>
          <button type="button" [disabled]="busy()" (click)="remove(overlay.id)">Delete</button>
        </li>
      } @empty {
        <li class="quiet">No overlays yet.</li>
      }
    </ul>
    <button type="button" (click)="add()">New overlay</button>
    @if (editing(); as shown) {
      <fieldset class="form">
        <legend>{{ shown.id }}</legend>
        <label>Name <input aria-label="Overlay name" [value]="shown.name" (input)="name($event)" /></label>
        <label>From <input type="date" aria-label="Overlay starts" [value]="fields().start" (change)="dates('start', $event)" /></label>
        <label>Until <input type="date" aria-label="Overlay ends (this day is off)" [value]="fields().end" (change)="dates('end', $event)" /></label>
        <label><input type="checkbox" aria-label="Repeats every year" [checked]="fields().yearly" (change)="yearly($event)" /> Repeats every year</label>
        <p class="quiet" aria-live="polite">{{ windowText() }}</p>
        <div class="strip" aria-hidden="true">
          @for (days of strip(); track $index) {
            <span class="quiet">{{ months[$index] }} {{ days }}</span>
          }
        </div>
        <label>
          Look
          <select aria-label="Overlay look" (change)="lookChosen($event)">
            <option value="" [selected]="!shown.look">The room's own</option>
            @for (name of variants(); track name) {
              <option [value]="name" [selected]="shown.look === name">{{ name }}</option>
            }
          </select>
        </label>
        <label>Adds (JSON list of props) <textarea rows="6" spellcheck="false" aria-label="Overlay adds as JSON" [value]="addsText()" (change)="props('adds', $event)"></textarea></label>
        <label>Removes (JSON list of props) <textarea rows="4" spellcheck="false" aria-label="Overlay removes as JSON" [value]="removesText()" (change)="props('removes', $event)"></textarea></label>
        @if (problem()) {
          <p class="message" role="alert">{{ problem() }}</p>
        }
        <div class="actions">
          <button type="button" class="primary" [disabled]="busy()" (click)="save()">Save overlay</button>
          <button type="button" (click)="editing.set(null)">Close</button>
        </div>
      </fieldset>
    }
  `,
})
export class RoomOverlaysForm {
  private readonly api = inject(RoomsApi);
  readonly roomId = input.required<string>();
  readonly overlays = input.required<OverlayData[]>();
  readonly variants = input<string[]>([]);
  readonly previewing = output<string>();
  readonly saved = output<void>();
  protected readonly editing = signal<OverlayData | null>(null);
  protected readonly message = signal("");
  protected readonly problem = signal("");
  protected readonly busy = signal(false);
  protected readonly months = monthNames;
  protected readonly fields = computed<WindowFields>(() => {
    const shown = this.editing();
    return shown ? fieldsOf(shown.window) : { start: "", end: "", yearly: false };
  });
  protected readonly windowText = computed(() => windowProblem(this.fields()) ?? summary(this.fields()));
  protected readonly strip = computed(() => {
    const now = this.fields();
    return windowProblem(now) === null
      ? calendar(now, Number(now.start.slice(0, 4)) || new Date().getUTCFullYear()).map((days) => days.map((on) => (on ? "#" : ".")).join(""))
      : [];
  });
  protected readonly addsText = computed(() => JSON.stringify(this.editing()?.adds ?? [], null, 2));
  protected readonly removesText = computed(() => JSON.stringify(this.editing()?.removes ?? [], null, 2));

  protected edit(overlay: OverlayData): void {
    this.problem.set("");
    this.editing.set(overlay);
  }

  protected add(): void {
    this.edit(blankOverlay(`overlay-${this.overlays().length + 1}`));
  }

  protected name(event: Event): void {
    this.update((overlay) => ({ ...overlay, name: (event.target as HTMLInputElement).value }));
  }

  protected dates(which: "start" | "end", event: Event): void {
    this.update((overlay) => withWindow(overlay, { ...fieldsOf(overlay.window), [which]: (event.target as HTMLInputElement).value }));
  }

  protected yearly(event: Event): void {
    this.update((overlay) => withWindow(overlay, { ...fieldsOf(overlay.window), yearly: (event.target as HTMLInputElement).checked }));
  }

  protected lookChosen(event: Event): void {
    this.update((overlay) => withLook(overlay, (event.target as HTMLSelectElement).value));
  }

  protected props(which: "adds" | "removes", event: Event): void {
    const read = propsFrom((event.target as HTMLTextAreaElement).value, which === "adds" ? "Adds" : "Removes");
    if ("problem" in read) {
      this.problem.set(read.problem);
      return;
    }
    this.problem.set("");
    this.update((overlay) => ({ ...overlay, [which]: read.props }));
  }

  protected save(): Promise<void> {
    const overlay = this.editing();
    if (!overlay) return Promise.resolve();
    const windowFault = windowProblem(fieldsOf(overlay.window));
    if (windowFault !== null) {
      this.problem.set(windowFault);
      return Promise.resolve();
    }
    return this.act(async () => {
      await this.api.saveOverlay(this.roomId(), overlay.id, overlay);
      this.saved.emit();
      return `${overlay.name} is saved.`;
    });
  }

  protected remove(id: string): Promise<void> {
    return this.act(async () => {
      await this.api.deleteOverlay(this.roomId(), id);
      if (this.editing()?.id === id) this.editing.set(null);
      this.saved.emit();
      return `${id} is gone.`;
    });
  }

  private update(change: (overlay: OverlayData) => OverlayData): void {
    this.editing.update((overlay) => (overlay ? change(overlay) : overlay));
  }

  private async act(action: () => Promise<string>): Promise<void> {
    this.busy.set(true);
    try {
      this.message.set(await action());
      this.problem.set("");
    } catch (error) {
      this.problem.set(messageOf(error));
    } finally {
      this.busy.set(false);
    }
  }
}

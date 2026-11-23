import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from "@angular/core";
import { Router, RouterLink } from "@angular/router";
import { newRoomTemplate } from "../../src/roomCanvas";
import { parseJson, problemsText, type RoomDetail } from "../../src/roomsData";
import { messageOf } from "./http";
import { RoomsApi } from "./roomsApi";

type Section = "layout" | "versions";

@Component({
  selector: "app-room-editor",
  imports: [RouterLink],
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
        <a href="" [class.current]="section() === 'versions'" (click)="show($event, 'versions')">Versions</a>
      </nav>
      <section class="card">
        @if (section() === "layout") {
          <h2>Layout</h2>
          <textarea rows="26" spellcheck="false" [attr.aria-label]="id() + ' layout as JSON'" [value]="layoutText()" (input)="typed($event)"></textarea>
          <div class="actions">
            <button type="button" class="primary" [disabled]="busy()" (click)="save()">Save draft</button>
            <button type="button" [disabled]="busy() || !detail()?.draft" (click)="publish()">Publish draft</button>
            <button type="button" [disabled]="busy() || !detail()?.draft" (click)="discard()">Discard draft</button>
          </div>
          <label>
            Publish together with
            <input aria-label="Publish together with these rooms" placeholder="room-a, room-b" [value]="together()" (input)="togetherTyped($event)" />
          </label>
          <p class="quiet">
            The top-down picture, the live 3D preview, the look, variants, overlays, painters and wildlife are still on the
            <a href="/admin">old admin page</a> while they are moved here.
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

  constructor() {
    effect(() => {
      const id = this.id();
      const fresh = this.isNew() !== undefined;
      void this.load(id, fresh);
    });
  }

  protected show(event: Event, section: Section): void {
    event.preventDefault();
    this.section.set(section);
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

  protected publish(): Promise<void> {
    return this.act(async () => {
      await this.api.publish(this.id(), this.together());
      await this.load(this.id(), false);
      return `${this.id()} is published.`;
    });
  }

  protected discard(): Promise<void> {
    return this.act(async () => {
      const neverLive = this.detail()?.published == null;
      await this.api.discardDraft(this.id());
      if (neverLive) {
        await this.router.navigate(["/rooms"]);
        return "The draft is gone.";
      }
      await this.load(this.id(), false);
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
      this.layoutText.set(JSON.stringify(detail?.draft ?? detail?.published ?? newRoomTemplate(id), null, 2));
    } catch (error) {
      this.message.set(messageOf(error));
    }
  }
}

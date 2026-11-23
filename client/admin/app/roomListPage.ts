import { ChangeDetectionStrategy, Component, computed, inject, signal } from "@angular/core";
import { Router, RouterLink } from "@angular/router";
import { auditText, draftSetUrl, exportUrl, goLiveInstant, importText, roomLine, type AuditLine, type RoomSummary } from "../../src/roomsData";
import { messageOf } from "./http";
import { RoomsApi } from "./roomsApi";

@Component({
  selector: "app-room-list",
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1>Rooms</h1>
    <p class="quiet">Pick a room to edit it. Saving makes a draft; publishing checks every room together first.</p>
    @if (message()) {
      <p class="message" role="status">{{ message() }}</p>
    }
    <ul class="rooms">
      @for (room of rooms(); track room.roomId) {
        <li>
          <a class="room card" [routerLink]="['/rooms', room.roomId]" [attr.aria-label]="line(room)">
            <strong>{{ room.name }}</strong>
            <span>
              @if (room.published === null) {
                <span class="badge">not published</span>
              } @else {
                <span class="badge live">version {{ room.published }} live</span>
              }
              @if (room.draft !== null) {
                <span class="badge draft">draft {{ room.draft }}</span>
              }
            </span>
          </a>
        </li>
      }
    </ul>
    <form class="card actions" (submit)="create($event)">
      <input name="newRoom" aria-label="New room ID" placeholder="new-room-id" [value]="newRoom()" (input)="newRoom.set($any($event.target).value)" />
      <button type="submit" [disabled]="newRoom().trim() === ''">New room</button>
    </form>
    @if (waiting().length > 0) {
      <section class="card">
        <h2>Drafts waiting</h2>
        <p class="quiet">Tick some drafts to publish just those together (they are checked as a set), now or at a time.</p>
        @for (room of waiting(); track room.roomId) {
          <label><input type="checkbox" [attr.aria-label]="'Publish ' + room.roomId" [checked]="ticked().includes(room.roomId)" (change)="tick(room.roomId, $event)" /> {{ line(room) }}</label>
        }
        <div class="actions">
          <button type="button" [disabled]="busy()" (click)="publishTicked(false)">Publish ticked</button>
          <input type="datetime-local" aria-label="Go live at (your time)" (input)="goLive.set($any($event.target).value)" />
          <button type="button" [disabled]="busy()" (click)="publishTicked(true)">Publish ticked at that time</button>
        </div>
      </section>
    }
    <section class="card">
      <h2>World map</h2>
      <p class="quiet">Where the outdoor rooms sit on the world grid.</p>
      <a routerLink="/map">Open the world map</a>
    </section>
    <section class="card">
      <h2>Content file</h2>
      <p><a [href]="exportUrl" download="content.json">Download everything as JSON (rooms, looks, recipes, prop designs)</a></p>
      <button type="button" [disabled]="busy()" (click)="importContent()">Load the reviewed content as drafts</button>
    </section>
    <section class="card">
      <h2>What admins changed</h2>
      <button type="button" (click)="loadHistory()">Show the latest changes</button>
      <ul class="rooms">
        @for (line of history(); track $index) {
          <li>{{ auditLine(line) }}</li>
        }
      </ul>
    </section>
  `,
})
export class RoomListPage {
  private readonly api = inject(RoomsApi);
  private readonly router = inject(Router);
  protected readonly rooms = signal<RoomSummary[]>([]);
  protected readonly message = signal("");
  protected readonly newRoom = signal("");
  protected readonly line = roomLine;
  protected readonly exportUrl = exportUrl;
  protected readonly auditLine = auditText;
  protected readonly busy = signal(false);
  protected readonly ticked = signal<string[]>([]);
  protected readonly goLive = signal("");
  protected readonly history = signal<AuditLine[]>([]);
  protected readonly waiting = computed(() => this.rooms().filter((room) => room.draft !== null));

  constructor() {
    void this.reload();
  }

  private async reload(): Promise<void> {
    try {
      this.rooms.set(await this.api.list());
      this.ticked.update((ids) => ids.filter((id) => this.rooms().some((room) => room.roomId === id && room.draft !== null)));
    } catch (error) {
      this.message.set(messageOf(error));
    }
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

  protected tick(id: string, event: Event): void {
    const on = (event.target as HTMLInputElement).checked;
    this.ticked.update((ids) => (on ? [...ids, id] : ids.filter((other) => other !== id)));
  }

  protected publishTicked(later: boolean): Promise<void> {
    return this.act(async () => {
      const when = later ? goLiveInstant(this.goLive()) : undefined;
      if (later && when === null) return "Pick the time to go live at.";
      const set = draftSetUrl(this.ticked(), when ?? undefined);
      if (set === null) return "Tick the drafts to publish.";
      const ids = this.ticked().join(", ");
      await this.api.publish(set.roomId, this.ticked().slice(1).join(","), when ?? undefined);
      await this.reload();
      return later ? `${ids} will go live at ${this.goLive()}.` : `${ids} published together.`;
    });
  }

  protected importContent(): Promise<void> {
    return this.act(async () => {
      const text = importText(await this.api.importContent());
      await this.reload();
      return text;
    });
  }

  protected loadHistory(): Promise<void> {
    return this.act(async () => {
      this.history.set(await this.api.audit());
      return "";
    });
  }

  protected create(event: Event): void {
    event.preventDefault();
    const id = this.newRoom().trim();
    if (id !== "") void this.router.navigate(["/rooms", id], { queryParams: { new: 1 } });
  }
}

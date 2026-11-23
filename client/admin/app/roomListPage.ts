import { ChangeDetectionStrategy, Component, inject, signal } from "@angular/core";
import { Router, RouterLink } from "@angular/router";
import { roomLine, type RoomSummary } from "../../src/roomsData";
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
  `,
})
export class RoomListPage {
  private readonly api = inject(RoomsApi);
  private readonly router = inject(Router);
  protected readonly rooms = signal<RoomSummary[]>([]);
  protected readonly message = signal("");
  protected readonly newRoom = signal("");
  protected readonly line = roomLine;

  constructor() {
    this.api.list().then((rooms) => this.rooms.set(rooms), (error: unknown) => this.message.set(messageOf(error)));
  }

  protected create(event: Event): void {
    event.preventDefault();
    const id = this.newRoom().trim();
    if (id !== "") void this.router.navigate(["/rooms", id], { queryParams: { new: 1 } });
  }
}

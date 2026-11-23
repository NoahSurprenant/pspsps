import { ChangeDetectionStrategy, Component, effect, ElementRef, input, output, untracked, viewChild } from "@angular/core";
import { type CanvasDoor, type CanvasLayout, roomCanvas } from "../../src/roomCanvas";

/** The top-down picture of a room (src/roomCanvas.ts), made again when the layout is changed from outside it. */
@Component({
  selector: "app-room-canvas",
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<div #host></div>`,
})
export class RoomCanvasHost {
  readonly layout = input.required<CanvasLayout>();
  readonly roomId = input.required<string>();
  readonly propKinds = input<readonly string[]>([]);
  readonly rooms = input<readonly string[]>([]);
  readonly addWayBack = input<(door: CanvasDoor, back: CanvasDoor) => Promise<void>>();
  readonly changed = output<CanvasLayout>();
  private readonly host = viewChild.required<ElementRef<HTMLElement>>("host");
  private shown = "";

  constructor() {
    effect(() => {
      const layout = this.layout();
      const kinds = this.propKinds();
      const rooms = this.rooms();
      const addDoorPair = this.addWayBack();
      const roomId = this.roomId();
      // What the picture itself just reported comes back as the layout: leave it be (it has its own undo history).
      if (JSON.stringify(layout) === this.shown) return;
      untracked(() => {
        this.shown = JSON.stringify(layout);
        this.host().nativeElement.replaceChildren(roomCanvas(layout, kinds, (next) => {
          this.shown = JSON.stringify(next);
          this.changed.emit(next);
        }, { addDoorPair, rooms, roomId }));
      });
    });
  }
}

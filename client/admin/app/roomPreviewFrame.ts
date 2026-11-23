import { ChangeDetectionStrategy, Component, effect, ElementRef, inject, input, signal, viewChild } from "@angular/core";
import { PREVIEW_READY, type PreviewMessage } from "../../src/previewMessage";
import { messageOf } from "./http";
import { RoomsApi } from "./roomsApi";

/** How long the layout must sit still before the preview is asked for again (every key and drag would be a request). */
export const PREVIEW_DELAY_MS = 400;

/** The real 3D room (the game's own scene, on its own page in a frame) showing the layout as it is typed, saved or not. */
@Component({
  selector: "app-room-preview",
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { "(window:message)": "onMessage($event)" },
  template: `
    <iframe #frame title="3D preview of the room" src="/preview.html" class="preview-frame"></iframe>
    @if (problem()) {
      <p class="message" role="alert">The preview can't show this yet: {{ problem() }}</p>
    }
  `,
})
export class RoomPreviewFrame {
  private readonly api = inject(RoomsApi);
  readonly roomId = input.required<string>();
  readonly layout = input.required<unknown>();
  readonly look = input<unknown>(null);
  readonly overlay = input<string | undefined>(undefined);
  protected readonly problem = signal("");
  private readonly frame = viewChild.required<ElementRef<HTMLIFrameElement>>("frame");
  private ready = false;
  private waiting: PreviewMessage | null = null;
  private asked = 0;

  constructor() {
    effect((onCleanup) => {
      const roomId = this.roomId();
      const layout = this.layout();
      const look = this.look();
      const overlay = this.overlay();
      const timer = setTimeout(() => void this.refresh(roomId, layout, look, overlay), PREVIEW_DELAY_MS);
      onCleanup(() => clearTimeout(timer));
    });
  }

  protected onMessage(event: MessageEvent): void {
    const ours = event.source === this.frame().nativeElement.contentWindow && event.origin === window.location.origin;
    if (ours && (event.data as { type?: string } | null)?.type === PREVIEW_READY.type) {
      this.ready = true;
      this.send();
    }
  }

  private async refresh(roomId: string, layout: unknown, look: unknown, overlay?: string): Promise<void> {
    const mine = ++this.asked;
    try {
      const { draftLook, ...snapshot } = await this.api.preview(roomId, layout, look, overlay);
      if (mine !== this.asked) return;
      this.problem.set("");
      this.waiting = { type: "room-preview", roomId, snapshot, draftLook };
      this.send();
    } catch (error) {
      if (mine === this.asked) this.problem.set(messageOf(error));
    }
  }

  private send(): void {
    if (this.ready && this.waiting) {
      this.frame().nativeElement.contentWindow?.postMessage(this.waiting, window.location.origin);
    }
  }
}

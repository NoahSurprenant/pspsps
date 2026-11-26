import { asPreviewMessage, PREVIEW_READY } from "./previewMessage";
import { roomPreview } from "./roomPreview";

const canvas = document.getElementById("view") as HTMLCanvasElement;
const preview = roomPreview(canvas);
let latest = 0;

// Only the page that framed this one (the room editor, the same origin) may send it a room to draw.
window.addEventListener("message", (event) => {
  const message = event.origin === window.location.origin ? asPreviewMessage(event.data) : null;
  if (message) {
    const mine = ++latest;
    void preview.draw(message.roomId, message.snapshot, message.draftLook).catch((error: unknown) => {
      if (mine === latest) {
        console.error("The preview could not be drawn.", error);
      }
    });
  }
});

window.parent.postMessage(PREVIEW_READY, window.location.origin);

// ?room=<id>&overlay=<id> draws that room as the server would give it, for a picture of an overlay out of season.
const asked = new URLSearchParams(window.location.search);
const askedRoom = asked.get("room");
if (askedRoom) {
  void preview.show(askedRoom, asked.get("overlay") ?? undefined).catch((error: unknown) => {
    console.error("The preview could not be drawn.", error);
  });
}

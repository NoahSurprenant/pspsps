interface Point {
  x: number;
  y: number;
}

/** Turns a single tap into a room action, or a two-finger gesture into camera zoom. */
export function roomGestures(canvas: HTMLCanvasElement, tap: (x: number, y: number) => void, zoom: (factor: number) => void): void {
  const fingers = new Map<number, Point>();
  let tapStart: Point | undefined;
  let distance = 0;
  const point = (event: PointerEvent): Point => ({ x: event.clientX, y: event.clientY });
  const gap = (): number => {
    const [first, second] = fingers.values();
    return fingers.size === 2 && first && second ? Math.hypot(first.x - second.x, first.y - second.y) : 0;
  };
  const nearStart = (position: Point): boolean =>
    tapStart !== undefined && Math.hypot(position.x - tapStart.x, position.y - tapStart.y) <= 8;

  canvas.addEventListener("pointerdown", (event) => {
    if (event.pointerType !== "touch") {
      return;
    }
    const start = point(event);
    fingers.set(event.pointerId, start);
    tapStart = fingers.size === 1 ? start : undefined;
    distance = gap();
    canvas.setPointerCapture(event.pointerId);
  });

  canvas.addEventListener("pointermove", (event) => {
    if (!fingers.has(event.pointerId)) {
      return;
    }
    const position = point(event);
    fingers.set(event.pointerId, position);
    if (!nearStart(position)) {
      tapStart = undefined;
    }
    const next = gap();
    if (next >= 8 && distance >= 8) {
      zoom(next / distance);
    }
    distance = next;
  });

  const finish = (event: PointerEvent): void => {
    if (!fingers.delete(event.pointerId)) {
      return;
    }
    const position = point(event);
    if (event.type === "pointerup" && nearStart(position)) {
      const bounds = canvas.getBoundingClientRect();
      tap(position.x - bounds.left, position.y - bounds.top);
    }
    tapStart = undefined;
    distance = gap();
  };
  canvas.addEventListener("pointerup", finish);
  canvas.addEventListener("pointercancel", finish);
  canvas.addEventListener("lostpointercapture", finish);

  canvas.addEventListener("wheel", (event) => {
    event.preventDefault();
    const unit = event.deltaMode === WheelEvent.DOM_DELTA_LINE ? 16
      : event.deltaMode === WheelEvent.DOM_DELTA_PAGE ? canvas.clientHeight : 1;
    const delta = Math.max(-250, Math.min(250, event.deltaY * unit));
    zoom(Math.exp(-delta * 0.004));
  }, { passive: false });
}

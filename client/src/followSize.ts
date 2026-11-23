/** Sizes the map's 3D picture now and whenever its box changes (it has no size until the sheet is laid out). Returns how to stop. */
export function followSize(canvas: HTMLCanvasElement, resize: () => void): () => void {
  const observer = new ResizeObserver(resize);
  observer.observe(canvas);
  resize();
  return () => observer.disconnect();
}

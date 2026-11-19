import catGlb from "./models/cat.glb?inline";

/** The real cat model's bytes, from the data URL Vite makes of cat.glb: what every test that loads it decodes. */
export function modelBytes(): Uint8Array {
  return Uint8Array.from(atob(catGlb.slice(catGlb.indexOf(",") + 1)), (c) => c.charCodeAt(0));
}

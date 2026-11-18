// Building the page's HTML in code, shared by the game's overlay (ui.ts) and the admin page (adminPage.ts): nothing of
// the 3D scene, so the admin page loads none of it.

/** An element <tag> with its text, class and ID (each when given), holding <children>. */
export function el<K extends keyof HTMLElementTagNameMap>(
  tag: K, options: { text?: string; className?: string; id?: string } = {}, ...children: Node[]
): HTMLElementTagNameMap[K] {
  const element = document.createElement(tag);
  if (options.text) element.textContent = options.text;
  if (options.className) element.className = options.className;
  if (options.id) element.id = options.id;
  element.append(...children);
  return element;
}

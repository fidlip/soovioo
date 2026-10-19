/** Finds the human label of a form control's host element: an explicit
 * `<label for>` (also via an inner element's id — optimus components put
 * `inputId` on their inner input), a wrapping `<label>`, or as a fallback
 * the placeholder / aria-label. Used for the required marker and for the
 * "Vyplňte: …" hint on a disabled submit button. */
export function labelElementFor(host: Element): HTMLLabelElement | null {
  const form = host.closest('form') ?? host.ownerDocument;
  const ids = [host.id, ...Array.from(host.querySelectorAll('[id]'), (e) => e.id)]
    .filter((id) => !!id);
  for (const id of ids) {
    const label = form.querySelector<HTMLLabelElement>(
      `label[for="${cssEscape(id)}"]`,
    );
    if (label) {
      return label;
    }
  }
  return host.closest('label');
}

export function fieldLabelText(host: Element): string {
  const label = labelElementFor(host);
  const text =
    label?.textContent ??
    host.getAttribute('placeholder') ??
    host.querySelector('[placeholder]')?.getAttribute('placeholder') ??
    host.getAttribute('aria-label') ??
    host.getAttribute('name') ??
    '';
  return text.replace(/\s*\*\s*$/, '').replace(/\s+/g, ' ').trim();
}

/** `CSS.escape` where available (browsers); a minimal fallback for test
 * environments without the CSS object — ids/names here are plain words. */
export function cssEscape(value: string): string {
  return typeof CSS !== 'undefined' && CSS.escape
    ? CSS.escape(value)
    : value.replace(/["\\]/g, '\\$&');
}

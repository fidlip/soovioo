/** The visitor's demo, remembered by the entry page. localStorage is per
 * origin, so it lives on `demo.<mainDomain>` — the demo school itself (its
 * own subdomain) never needs to touch it: the entry page checks the demo
 * still exists before sending anyone there. */
const KEY = 'soovioo-demo-slug';

export function storedDemoSlug(): string | null {
  try {
    return localStorage.getItem(KEY);
  } catch {
    return null;
  }
}

export function storeDemoSlug(slug: string | null): void {
  try {
    if (slug) {
      localStorage.setItem(KEY, slug);
    } else {
      localStorage.removeItem(KEY);
    }
  } catch {
    // Blocked storage — the visitor just gets the wizard next time.
  }
}

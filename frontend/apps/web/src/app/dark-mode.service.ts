import { Injectable, signal } from '@angular/core';

const DARK_MODE_STORAGE_KEY = 'soovioo-dark-mode';

/** Shared so both the header's quick toggle and the Nastavení page's switch
 * reflect and change the same state. */
@Injectable({ providedIn: 'root' })
export class DarkModeService {
  // Persisted choice wins; first-ever visit falls back to the OS preference
  // instead of always defaulting to light.
  readonly darkMode = signal(this.readInitial());

  constructor() {
    this.apply(this.darkMode());
  }

  toggle(): void {
    this.set(!this.darkMode());
  }

  set(enabled: boolean): void {
    this.darkMode.set(enabled);
    this.apply(enabled);
    try {
      localStorage.setItem(DARK_MODE_STORAGE_KEY, String(enabled));
    } catch {
      // Private browsing / blocked storage — the toggle still works for
      // this page load, it just won't survive a reload.
    }
  }

  private readInitial(): boolean {
    try {
      const stored = localStorage.getItem(DARK_MODE_STORAGE_KEY);
      if (stored !== null) {
        return stored === 'true';
      }
    } catch {
      // Fall through to the OS preference below.
    }
    return window.matchMedia?.('(prefers-color-scheme: dark)').matches ?? false;
  }

  private apply(enabled: boolean): void {
    document.documentElement.classList.toggle('soovioo-dark', enabled);
  }
}

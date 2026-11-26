const KEY = 'soovioo.supportTab';

/** This tab entered the school in support mode (from the backoffice) — kept
 * in the school page's tab (its own origin in production) for the whole
 * support session. */
export function markSupportTab(): void {
  try {
    sessionStorage.setItem(KEY, '1');
  } catch {
    // Without storage it ends at the login form.
  }
}

/** This tab is (or was) in support mode. */
export function isSupportTab(): boolean {
  try {
    return sessionStorage.getItem(KEY) !== null;
  } catch {
    return false;
  }
}

export function forgetSupportTab(): void {
  try {
    sessionStorage.removeItem(KEY);
  } catch {
    // Nothing to forget.
  }
}

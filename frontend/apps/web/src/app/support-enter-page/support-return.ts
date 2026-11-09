const KEY = 'soovioo.supportReturn';
/** Only back into the SuperAdmin area, e.g. `superadmin/support?ticket=5`. */
const SAFE = /^superadmin(\/[a-z-]+)*(\?[A-Za-z]+=\d+)?$/;

/** Where "Opustit" leads: the SuperAdmin page entering started from, with
 * the row to point at. Kept in the school page's tab (its own origin in
 * production) for the whole support session. */
export function rememberSupportReturn(path: string | null): void {
  try {
    if (path && SAFE.test(path)) {
      sessionStorage.setItem(KEY, path);
    } else {
      sessionStorage.removeItem(KEY);
    }
  } catch {
    // Without storage it's back to Správa škol.
  }
}

/** This tab is (or was) in support mode — entered with a way back. */
export function hasSupportReturn(): boolean {
  try {
    return sessionStorage.getItem(KEY) !== null;
  } catch {
    return false;
  }
}

export function supportReturnPath(): string {
  try {
    const path = sessionStorage.getItem(KEY);
    sessionStorage.removeItem(KEY);
    return path && SAFE.test(path) ? path : 'superadmin';
  } catch {
    return 'superadmin';
  }
}

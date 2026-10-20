import { Injectable, inject, signal } from '@angular/core';
import { NavigationEnd, Router } from '@angular/router';
import { filter } from 'rxjs';
import { environment } from '../environments/environment';

/** Dev fallback when no school is known yet (bare `/`). */
export const DEFAULT_DEV_SLUG = 'skola';

/**
 * Only the real deployment domain carries the school in a subdomain
 * (`skola.<mainDomain>/grades`, routes unprefixed). Every other host — dev
 * and LAN access via `localhost`, a bare IP, or a machine name like `aorus`
 * / `aorus.local` from a phone — has no subdomain to read, so the slug lives
 * in the first path segment instead (`/skola/grades`). Deciding by "is it the
 * main domain" rather than listing dev hosts means no LAN name can fall into
 * subdomain mode, where `/skola/login` doesn't exist and redirects to `/`.
 */
export function isPathBasedTenant(
  hostname: string = window.location.hostname,
  mainDomain: string = environment.mainDomain,
): boolean {
  return hostname !== mainDomain && !hostname.endsWith(`.${mainDomain}`);
}

/** First label of the subdomain in front of `mainDomain`, e.g. "demo" from
 * "demo.soovioo.cloud" when `mainDomain` is "soovioo.cloud". The bare main domain itself
 * (no subdomain) has no tenant — that's the landing/SuperAdmin entry point,
 * not a school. Matched directly against the configured domain (not guessed
 * from segment count), so it works regardless of how many labels the real
 * domain turns out to have (`soovioo.cloud` vs e.g. `soovioo.co.uk`). A hostname that
 * doesn't end in `mainDomain` at all (misconfiguration, or mid-migration to
 * a new domain) fails closed to "no tenant" rather than guessing. */
export function subdomainSlug(hostname: string, mainDomain: string = environment.mainDomain): string | null {
  const suffix = `.${mainDomain}`;
  if (!hostname.endsWith(suffix)) {
    return null;
  }
  return hostname.slice(0, -suffix.length).split('.')[0] || null;
}

/** The school the current page belongs to, read straight from the URL (no
 * router needed — also used at startup, before routing): the first path
 * segment in path mode (except the SuperAdmin area), the subdomain
 * otherwise. */
export function currentSchoolSlugFromUrl(
  location: Pick<Location, 'hostname' | 'pathname'> = window.location,
): string | null {
  if (!isPathBasedTenant(location.hostname)) {
    return subdomainSlug(location.hostname);
  }
  const first = location.pathname.split('/').filter(Boolean)[0] ?? null;
  return first === 'superadmin' ? null : first;
}

/** The "Vyzkoušet" entry page: `demo.<mainDomain>` (path mode `/demo`).
 * Not a school — the demo schools themselves live at `demo-<id>`. */
export const DEMO_ENTRY_SLUG = 'demo';

/** Demo schools' slugs, `demo-<random id>`. */
export function isDemoSlug(slug: string | null): boolean {
  return !!slug && slug.startsWith(`${DEMO_ENTRY_SLUG}-`);
}

/** Subdomain mode on `demo.<mainDomain>` — the whole app is the entry page. */
export function isDemoEntryHost(hostname: string = window.location.hostname): boolean {
  return !isPathBasedTenant(hostname) && subdomainSlug(hostname) === DEMO_ENTRY_SLUG;
}

/** Absolute URL of the demo entry page, e.g. `…?reset=demo-abc`. */
export function demoEntryUrl(query = ''): string {
  return schoolUrl(DEMO_ENTRY_SLUG, query ? `?${query}` : '').replace('/?', '?');
}

/** Absolute URL of a page in the SuperAdmin area (`/superadmin…` on the
 * main domain in production). */
export function superAdminUrl(path = 'superadmin'): string {
  if (isPathBasedTenant()) {
    return `/${path}`;
  }
  const { protocol, port } = window.location;
  return `${protocol}//${environment.mainDomain}${port ? `:${port}` : ''}/${path}`;
}

/** Absolute URL of a page of school `slug` — its own subdomain in production. */
export function schoolUrl(slug: string, path: string): string {
  if (isPathBasedTenant()) {
    return `/${slug}/${path}`;
  }
  const { protocol, port } = window.location;
  return `${protocol}//${slug}.${environment.mainDomain}${port ? `:${port}` : ''}/${path}`;
}

@Injectable({ providedIn: 'root' })
export class SchoolContextService {
  private readonly router = inject(Router);

  readonly isPathBased = isPathBasedTenant();

  private readonly slugSignal = signal<string | null>(
    this.isPathBased ? null : subdomainSlug(window.location.hostname),
  );
  readonly slug = this.slugSignal.asReadonly();

  constructor() {
    // Subdomain mode: the slug is fixed for the lifetime of the page (it's
    // part of the hostname), nothing to keep in sync. Path mode: re-read it
    // from the route every time navigation settles, since it can change
    // (e.g. a direct link into a different school).
    if (this.isPathBased) {
      this.router.events
        .pipe(filter((e): e is NavigationEnd => e instanceof NavigationEnd))
        .subscribe(() => this.slugSignal.set(this.deepestSlugParam()));
    }
  }

  /** Builds an absolute router link that stays within the current school. */
  link(path: string): string[] {
    return this.isPathBased ? ['/', this.slugSignal() ?? DEFAULT_DEV_SLUG, path] : ['/', path];
  }

  private deepestSlugParam(): string | null {
    let node = this.router.routerState.snapshot.root;
    let slug: string | null = null;
    while (node) {
      slug = node.paramMap.get('schoolSlug') ?? slug;
      if (!node.firstChild) {
        break;
      }
      node = node.firstChild;
    }
    return slug;
  }
}

import { Injectable, inject, signal } from '@angular/core';
import { NavigationEnd, Router } from '@angular/router';
import { filter } from 'rxjs';

/** Dev fallback when no school is known yet (bare `/`). */
export const DEFAULT_DEV_SLUG = 'demo';

/**
 * Dev/LAN access (`localhost` or a bare IP) has no real subdomain to carry
 * the school, so the slug lives in the first path segment instead
 * (`/demo/grades`). A real deployment resolves it from the subdomain
 * (`demo.maindomain.cz/grades`) and routes stay unprefixed.
 */
export function isPathBasedTenant(hostname: string = window.location.hostname): boolean {
  return hostname === 'localhost' || /^\d{1,3}(\.\d{1,3}){3}$/.test(hostname);
}

/** First label of the hostname, e.g. "demo" from "demo.maindomain.cz".
 * A bare two-label domain (the main domain itself, no subdomain) has no
 * tenant — that's the future landing/SuperAdmin entry point, not a school. */
function subdomainSlug(hostname: string): string | null {
  const parts = hostname.split('.');
  return parts.length > 2 ? parts[0] : null;
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

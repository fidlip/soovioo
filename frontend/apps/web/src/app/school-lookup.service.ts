import { HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { PublicSchool, PublicSchoolsApiService } from '@soovioo/shared';
import { Observable, catchError, map, of, shareReplay, throwError } from 'rxjs';

/** Public info about a school slug, asked once per slug and page load (the
 * guard runs on every navigation). Emits null when the slug belongs to no
 * school; other failures propagate, so callers can fail open. */
@Injectable({ providedIn: 'root' })
export class SchoolLookupService {
  private readonly api = inject(PublicSchoolsApiService);
  private readonly cache = new Map<string, Observable<PublicSchool | null>>();

  lookup(slug: string): Observable<PublicSchool | null> {
    let school$ = this.cache.get(slug);
    if (!school$) {
      school$ = this.api.getSchool(slug).pipe(
        map((school): PublicSchool | null => school),
        catchError((err: unknown) =>
          err instanceof HttpErrorResponse && err.status === 404
            ? of(null)
            : throwError(() => err),
        ),
        shareReplay(1),
      );
      // A failed request isn't cached — the next navigation asks again.
      school$.subscribe({ error: () => this.cache.delete(slug) });
      this.cache.set(slug, school$);
    }
    return school$;
  }
}

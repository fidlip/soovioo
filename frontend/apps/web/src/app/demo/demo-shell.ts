import { Component, DestroyRef, inject } from '@angular/core';
import {
  takeUntilDestroyed,
  toObservable,
  toSignal,
} from '@angular/core/rxjs-interop';
import { AuthApiService } from '@soovioo/shared';
import { catchError, map, of, switchMap } from 'rxjs';
import { SchoolContextService, isDemoSlug } from '../school-context';
import { checkOnNavigation } from '../layout/check-on-navigation';
import { SchoolLookupService } from '../school-lookup.service';
import { DemoBar } from './demo-bar';
import { DemoEmailPreview } from './demo-email-preview';
import { DemoFeedbackDialog } from './demo-feedback-dialog';
import { DemoFeedbackService } from './demo-feedback.service';

/** Everything a "Vyzkoušet" demo school adds on top of the page: the demo
 * bar, the feedback prompt and the e-mail preview. */
@Component({
  selector: 'app-demo-shell',
  imports: [DemoBar, DemoEmailPreview, DemoFeedbackDialog],
  template: `
    @if (demoSchool(); as demo) {
      <app-demo-bar [slug]="demo.slug" [expiresAt]="demo.expiresAt" />
      @if (authApi.isLoggedIn()) {
        <app-demo-feedback-dialog [slug]="demo.slug" />
        <app-demo-email-preview />
      }
    }
  `,
  styles: ':host { display: contents; }',
})
export class DemoShell {
  protected readonly authApi = inject(AuthApiService);
  private readonly schoolContext = inject(SchoolContextService);
  private readonly schoolLookup = inject(SchoolLookupService);

  /** The current school when it's a demo. */
  protected readonly demoSchool = toSignal(
    toObservable(this.schoolContext.slug).pipe(
      switchMap((slug) =>
        slug && isDemoSlug(slug)
          ? this.schoolLookup.lookup(slug).pipe(
              map((school) => (school?.isDemo ? { ...school, slug } : null)),
              catchError(() => of(null)),
            )
          : of(null),
      ),
    ),
    { initialValue: null },
  );

  constructor() {
    checkOnNavigation();
    const destroyRef = inject(DestroyRef);
    const feedback = inject(DemoFeedbackService);
    toObservable(this.demoSchool)
      .pipe(takeUntilDestroyed())
      .subscribe((demo) => demo && feedback.start(destroyRef));
  }
}

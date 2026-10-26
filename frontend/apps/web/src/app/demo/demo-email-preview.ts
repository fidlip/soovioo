import { Component, Injectable, computed, inject, signal } from '@angular/core';
import { Dialog } from '@openng/optimus-ui/dialog';
import { DemoApiService, DemoEmail } from '@soovioo/shared';
import { NotifyService } from '../notify.service';
import { SchoolContextService, isDemoSlug } from '../school-context';

/** A demo school sends no e-mail at all (DemoAwareEmailSender keeps it on
 * the server) — instead of "odesláno" the app shows what would have gone
 * out, links included, so e.g. an invitation can be followed through. */
@Injectable({ providedIn: 'root' })
export class DemoEmailPreviewService {
  private readonly demoApi = inject(DemoApiService);
  private readonly notify = inject(NotifyService);
  private readonly schoolContext = inject(SchoolContextService);

  readonly email = signal<DemoEmail | null>(null);
  readonly active = computed(() => isDemoSlug(this.schoolContext.slug()));

  /** Outside a demo just the success toast; inside it, the preview. */
  sent(successText: string): void {
    if (!this.active()) {
      this.notify.success(successText);
      return;
    }
    this.demoApi.emails().subscribe({
      next: (emails) => this.email.set(emails[0] ?? null),
      error: (err) => this.notify.actionFailed('demo emails failed', err),
    });
  }
}

@Component({
  selector: 'app-demo-email-preview',
  imports: [Dialog],
  template: `
    <p-dialog
      [visible]="preview.email() !== null"
      (visibleChange)="!$event && preview.email.set(null)"
      header="V demu se e-maily neodesílají — náhled"
      [modal]="true"
      [dismissableMask]="true"
      styleClass="w-full max-w-xl"
    >
      @if (preview.email(); as email) {
        <dl class="m-0 mb-3 grid grid-cols-[auto_1fr] gap-x-3 gap-y-1 text-sm">
          <dt class="text-surface-500">Komu</dt>
          <dd class="m-0">{{ email.to }}</dd>
          <dt class="text-surface-500">Předmět</dt>
          <dd class="m-0 font-medium">{{ email.subject }}</dd>
        </dl>
        <div
          class="demo-email-body rounded-md border border-surface-200 p-3 text-sm [overflow-wrap:anywhere] dark:border-surface-700"
          [innerHTML]="body()"
        ></div>
      }
    </p-dialog>
  `,
})
export class DemoEmailPreview {
  protected readonly preview = inject(DemoEmailPreviewService);
  /** Our own server-rendered template (names HTML-encoded there);
   * Angular's [innerHTML] sanitizing still runs over it — links stay,
   * scripts wouldn't. */
  protected readonly body = computed(
    () => this.preview.email()?.htmlBody ?? '',
  );
}

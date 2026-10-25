import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { Button } from '@openng/optimus-ui/button';
import { Card } from '@openng/optimus-ui/card';
import { Message } from '@openng/optimus-ui/message';
import {
  NOTIFICATION_TYPE_LABELS,
  NotificationType,
  NotificationsApiService,
} from '@soovioo/shared';

/** Lands here from "Odhlásit tyto e-maily" in the daily e-mail — works
 * without logging in; a button, so a mail scanner opening the link doesn't
 * unsubscribe anyone. */
@Component({
  selector: 'app-unsubscribe-page',
  imports: [Button, Card, Message],
  templateUrl: './unsubscribe-page.html',
})
export class UnsubscribePage {
  private readonly notificationsApi = inject(NotificationsApiService);
  private readonly route = inject(ActivatedRoute);

  protected readonly token = this.route.snapshot.queryParamMap.get('token');

  /** Null until done; then the kinds that keep coming. */
  protected readonly forced = signal<NotificationType[] | null>(null);
  protected readonly forcedLabels = computed(() =>
    (this.forced() ?? []).map((t) => NOTIFICATION_TYPE_LABELS[t]).join(', '),
  );
  protected readonly error = signal<string | null>(null);
  protected readonly busy = signal(false);

  protected unsubscribe(): void {
    if (!this.token) {
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    this.notificationsApi.unsubscribe(this.token).subscribe({
      next: (result) => {
        this.busy.set(false);
        this.forced.set(result.forced);
      },
      error: (err) => {
        this.busy.set(false);
        if (err?.status !== 404) {
          console.error('unsubscribe failed', err);
        }
        this.error.set(
          err?.status === 404
            ? 'Odkaz je neplatný. Upozornění e-mailem můžete vypnout po přihlášení v Nastavení.'
            : 'Odhlášení se nepovedlo, zkuste to prosím znovu.',
        );
      },
    });
  }
}

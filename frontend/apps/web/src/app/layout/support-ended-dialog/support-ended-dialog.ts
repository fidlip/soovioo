import { Component, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Button } from '@openng/optimus-ui/button';
import { Dialog } from '@openng/optimus-ui/dialog';
import {
  AuthApiService,
  DateFormatPipe,
  SupportApiService,
} from '@soovioo/shared';
import { filter, interval, merge } from 'rxjs';
import { LiveService } from '../../live.service';
import { Markdown } from '@fidlip/soovioo-ui';
import { SessionActionsService } from '../session-actions.service';

/** How often support mode asks whether its session still runs while the
 * live connection is down. */
const CHECK_MS = 20_000;

/** Support mode ended by someone else (the school reset the changes,
 * cancelled the ticket, took the approval back…): who and why — the tab
 * goes back only once the SuperAdmin confirms it. Learnt from the live
 * connection, the next request, or asking now and then without it. */
@Component({
  selector: 'app-support-ended-dialog',
  imports: [Button, DateFormatPipe, Dialog, Markdown],
  templateUrl: './support-ended-dialog.html',
})
export class SupportEndedDialog {
  protected readonly authApi = inject(AuthApiService);
  private readonly sessionActions = inject(SessionActionsService);

  constructor() {
    const api = inject(SupportApiService);
    const live = inject(LiveService);
    merge(
      live.support$,
      interval(CHECK_MS).pipe(filter(() => !live.connected())),
    )
      .pipe(
        filter(() => !this.authApi.supportEnded()),
        takeUntilDestroyed(),
      )
      .subscribe(() =>
        api.checkCurrentSession().subscribe({ error: () => undefined }),
      );
  }

  protected confirm(): void {
    this.sessionActions.returnFromSupport();
  }
}

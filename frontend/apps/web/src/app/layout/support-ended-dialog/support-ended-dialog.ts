import { Component, DestroyRef, inject } from '@angular/core';
import { Button } from '@openng/optimus-ui/button';
import { Dialog } from '@openng/optimus-ui/dialog';
import {
  AuthApiService,
  DateFormatPipe,
  SupportApiService,
} from '@soovioo/shared';
import { Markdown } from '../../markdown/markdown';
import { SessionActionsService } from '../session-actions.service';

/** How often support mode asks whether its session still runs. */
const CHECK_MS = 20_000;

/** Support mode ended by someone else (the school reset the changes,
 * cancelled the ticket, took the approval back…): who and why — the tab
 * goes back only once the SuperAdmin confirms it. */
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
    const timer = setInterval(() => {
      if (!this.authApi.supportEnded()) {
        api.checkCurrentSession().subscribe({ error: () => undefined });
      }
    }, CHECK_MS);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }

  protected confirm(): void {
    this.sessionActions.returnFromSupport();
  }
}

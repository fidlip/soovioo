import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Button } from '@openng/optimus-ui/button';
import { Dialog } from '@openng/optimus-ui/dialog';
import { FORM_FIELDS } from '../../forms';
import { MarkdownEditor } from '../../markdown/markdown-editor';
import { SessionActionsService } from '../session-actions.service';

/** "Opustit" in support mode: what was done, if the SuperAdmin wants to
 * write it down — it goes to the ticket's thread (the school sees it) and
 * Logy. */
@Component({
  selector: 'app-support-leave-dialog',
  imports: [Button, Dialog, ...FORM_FIELDS, FormsModule, MarkdownEditor],
  templateUrl: './support-leave-dialog.html',
})
export class SupportLeaveDialog {
  private readonly sessionActions = inject(SessionActionsService);

  protected readonly visible = signal(false);
  protected readonly leaving = signal(false);
  protected result = '';

  open(): void {
    this.result = '';
    this.visible.set(true);
  }

  protected leave(): void {
    this.leaving.set(true);
    this.sessionActions.leaveSupport(this.result.trim() || undefined);
  }
}

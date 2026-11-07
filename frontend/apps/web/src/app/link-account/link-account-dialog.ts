import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  AutoComplete,
  AutoCompleteCompleteEvent,
} from '@openng/optimus-ui/autocomplete';
import { Button } from '@openng/optimus-ui/button';
import { Dialog } from '@openng/optimus-ui/dialog';
import { Message } from '@openng/optimus-ui/message';
import { LinkableSchool, LinkedAccountsApiService } from '@soovioo/shared';
import { FloatField } from '../forms';
import { schoolUrl } from '../school-context';
import { LinkAccountDialogService } from './link-account-dialog.service';

/** Picks the other school and goes to log in there; the password is typed
 * only on that school's own page. */
@Component({
  selector: 'app-link-account-dialog',
  imports: [AutoComplete, Button, Dialog, FloatField, FormsModule, Message],
  templateUrl: './link-account-dialog.html',
})
export class LinkAccountDialog {
  private readonly api = inject(LinkedAccountsApiService);
  protected readonly dialog = inject(LinkAccountDialogService);

  protected school: LinkableSchool | string | null = null;
  protected readonly suggestions = signal<LinkableSchool[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly starting = signal(false);

  protected search(event: AutoCompleteCompleteEvent): void {
    this.api.searchSchools(event.query).subscribe({
      next: (schools) => this.suggestions.set(schools),
      error: () => this.suggestions.set([]),
    });
  }

  /** A school from the list, not just text typed into the field. */
  protected chosen(): LinkableSchool | null {
    return this.school && typeof this.school === 'object' ? this.school : null;
  }

  protected proceed(): void {
    const school = this.chosen();
    if (!school) {
      return;
    }
    this.error.set(null);
    this.starting.set(true);
    this.api.startRequest(school.schoolSlug).subscribe({
      next: ({ code, schoolSlug }) => {
        window.location.href = `${schoolUrl(schoolSlug, 'account/link')}#code=${code}`;
      },
      error: (err: HttpErrorResponse) => {
        this.starting.set(false);
        this.error.set(
          typeof err.error === 'string' ? err.error : 'Propojení se nepovedlo.',
        );
      },
    });
  }
}

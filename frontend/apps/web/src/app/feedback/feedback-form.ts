import { Component, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Observable } from 'rxjs';
import { Button } from '@openng/optimus-ui/button';
import { Checkbox } from '@openng/optimus-ui/checkbox';
import { InputText } from '@openng/optimus-ui/inputtext';
import { Textarea } from '@openng/optimus-ui/textarea';
import { AuthApiService, FeedbackRequest } from '@soovioo/shared';
import { FloatField } from '../forms';
import { NotifyService } from '../notify.service';

/** Rating, text and a contact — the demo's feedback dialog and the
 * "Zpětná vazba" card in Nastavení. Starts over once sent. */
@Component({
  selector: 'app-feedback-form',
  imports: [FloatField, FormsModule, Button, Checkbox, InputText, Textarea],
  templateUrl: './feedback-form.html',
})
export class FeedbackForm {
  private readonly authApi = inject(AuthApiService);
  private readonly notify = inject(NotifyService);

  /** Where it goes (the demo's endpoint or the school user's). */
  readonly send =
    input.required<(request: FeedbackRequest) => Observable<void>>();
  /** Keeps the field ids apart when two forms could be on the page. */
  readonly idPrefix = input('feedback');
  readonly email = input('');
  readonly privacyUrl = input('https://soovioo.cz/zasady.html');
  readonly sent = output<void>();

  protected readonly ratings = [
    { value: 1, icon: '😞', label: 'Vůbec' },
    { value: 2, icon: '🙁', label: 'Spíš ne' },
    { value: 3, icon: '😐', label: 'Ujde to' },
    { value: 4, icon: '🙂', label: 'Líbí' },
    { value: 5, icon: '😍', label: 'Moc líbí' },
  ];

  protected readonly rating = signal<number | null>(null);
  protected text = '';
  protected contact: string | null = null;
  protected consent = false;
  protected readonly sending = signal(false);

  /** The account's e-mail until the user changes it. */
  protected contactValue(): string {
    return this.contact ?? this.email();
  }

  protected canSend(): boolean {
    return (
      (this.rating() !== null || this.text.trim().length > 0) && !this.sending()
    );
  }

  protected submit(): void {
    const email = this.contactValue().trim();
    this.sending.set(true);
    this.send()({
      rating: this.rating(),
      text: this.text.trim() || null,
      email: email || null,
      consent: this.consent && email.length > 0,
      role: this.authApi.getActiveRole()?.name ?? this.authApi.getRole(),
    }).subscribe({
      next: () => {
        this.sending.set(false);
        this.rating.set(null);
        this.text = '';
        this.contact = null;
        this.consent = false;
        this.notify.success('Děkujeme za zpětnou vazbu!');
        this.sent.emit();
      },
      error: (err) => {
        this.sending.set(false);
        this.notify.actionFailed('feedback failed', err);
      },
    });
  }
}

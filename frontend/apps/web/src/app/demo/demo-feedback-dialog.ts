import { Component, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Button } from '@openng/optimus-ui/button';
import { Checkbox } from '@openng/optimus-ui/checkbox';
import { Dialog } from '@openng/optimus-ui/dialog';
import { InputText } from '@openng/optimus-ui/inputtext';
import { Textarea } from '@openng/optimus-ui/textarea';
import { AuthApiService, DemoApiService } from '@soovioo/shared';
import { NotifyService } from '../notify.service';
import { DemoFeedbackService } from './demo-feedback.service';

/** "Jak se vám Soovioo líbí?" — opened by DemoFeedbackService. */
@Component({
  selector: 'app-demo-feedback-dialog',
  imports: [FormsModule, Button, Checkbox, Dialog, InputText, Textarea],
  templateUrl: './demo-feedback-dialog.html',
})
export class DemoFeedbackDialog {
  private readonly demoApi = inject(DemoApiService);
  private readonly authApi = inject(AuthApiService);
  private readonly notify = inject(NotifyService);
  protected readonly feedback = inject(DemoFeedbackService);

  readonly slug = input.required<string>();

  protected readonly ratings = [
    { value: 1, icon: '😞', label: 'Vůbec' },
    { value: 2, icon: '🙁', label: 'Spíš ne' },
    { value: 3, icon: '😐', label: 'Ujde to' },
    { value: 4, icon: '🙂', label: 'Líbí' },
    { value: 5, icon: '😍', label: 'Moc líbí' },
  ];

  protected readonly rating = signal<number | null>(null);
  protected text = '';
  protected email = '';
  protected consent = false;
  protected readonly sending = signal(false);

  protected canSend(): boolean {
    return (
      (this.rating() !== null || this.text.trim().length > 0) && !this.sending()
    );
  }

  protected send(): void {
    this.sending.set(true);
    this.demoApi
      .feedback(this.slug(), {
        rating: this.rating(),
        text: this.text.trim() || null,
        email: this.email.trim() || null,
        consent: this.consent && this.email.trim().length > 0,
        role: this.authApi.getActiveRole()?.name ?? this.authApi.getRole(),
      })
      .subscribe({
        next: () => {
          this.sending.set(false);
          this.feedback.mute();
          this.notify.success('Děkujeme za zpětnou vazbu!');
        },
        error: (err) => {
          this.sending.set(false);
          this.notify.actionFailed('demo feedback failed', err);
        },
      });
  }
}

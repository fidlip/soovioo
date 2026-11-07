import { Component, inject, input } from '@angular/core';
import { Button } from '@openng/optimus-ui/button';
import { Dialog } from '@openng/optimus-ui/dialog';
import { DemoApiService, FeedbackRequest } from '@soovioo/shared';
import { FeedbackForm } from '../feedback/feedback-form';
import { DemoFeedbackService } from './demo-feedback.service';

/** "Jak se vám Soovioo líbí?" — opened by DemoFeedbackService. */
@Component({
  selector: 'app-demo-feedback-dialog',
  imports: [Button, Dialog, FeedbackForm],
  templateUrl: './demo-feedback-dialog.html',
})
export class DemoFeedbackDialog {
  private readonly demoApi = inject(DemoApiService);
  protected readonly feedback = inject(DemoFeedbackService);

  readonly slug = input.required<string>();

  protected readonly send = (request: FeedbackRequest) =>
    this.demoApi.feedback(this.slug(), request);
}

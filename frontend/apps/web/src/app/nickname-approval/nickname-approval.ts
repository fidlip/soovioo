import { Component, inject, input, output } from '@angular/core';
import { Button } from '@openng/optimus-ui/button';
import { StudentsApiService } from '@soovioo/shared';
import { NotifyService } from '../notify.service';

/** A pupil's proposed nickname the school decides — the pupil has no
 * parent in the app (otherwise the parents do, in Mé děti). */
@Component({
  selector: 'app-nickname-approval',
  imports: [Button],
  templateUrl: './nickname-approval.html',
})
export class NicknameApproval {
  private readonly studentsApi = inject(StudentsApiService);
  private readonly notify = inject(NotifyService);

  readonly studentId = input.required<number>();
  readonly proposal = input.required<string>();
  readonly decided = output<void>();

  protected decide(approve: boolean): void {
    this.studentsApi.decideNickname(this.studentId(), approve).subscribe({
      next: () => {
        this.notify.success(approve ? 'Přezdívka schválena.' : 'Zamítnuto.');
        this.decided.emit();
      },
      error: (err) => this.notify.actionFailed('decideNickname failed', err),
    });
  }
}

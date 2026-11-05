import {
  Component,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Button } from '@openng/optimus-ui/button';
import { Checkbox } from '@openng/optimus-ui/checkbox';
import { Dialog } from '@openng/optimus-ui/dialog';
import { Select } from '@openng/optimus-ui/select';
import {
  RvpFillOptions,
  RvpFillSummary,
  RvpProgramCheck,
  RvpSubject,
  TeachingApiService,
  TeachingOverview,
  formatHours,
} from '@soovioo/shared';
import { NotifyService } from '../notify.service';

const AUTOMATIC = 'automatic';
const NO_AREA = 'none';

/** What a subject's choice of area in the program's select stands for. */
export function rvpAreaChoice(subject: RvpSubject): string {
  return subject.chosen ? (subject.areaKey ?? NO_AREA) : AUTOMATIC;
}

/** Učební plán against the RVPs of the classes, and filling it in a draft. */
@Component({
  selector: 'app-rvp-panel',
  imports: [Button, Checkbox, Dialog, FormsModule, Select],
  templateUrl: './rvp-panel.html',
})
export class RvpPanel {
  private readonly teachingApi = inject(TeachingApiService);
  private readonly notify = inject(NotifyService);

  protected readonly formatHours = formatHours;
  protected readonly areaChoice = rvpAreaChoice;

  readonly overview = input.required<TeachingOverview>();
  readonly changed = output<TeachingOverview>();

  protected readonly rvp = computed(() => this.overview().rvp);
  protected readonly canFill = computed(
    () => this.overview().canEdit && this.overview().isDraft,
  );
  protected readonly dialogVisible = signal(false);
  protected readonly busy = signal(false);
  protected readonly summary = signal<RvpFillSummary | null>(null);
  protected options: RvpFillOptions = {
    overwrite: false,
    createSubjects: true,
    spreadDisponible: true,
    removeOthers: false,
  };

  protected openDialog(): void {
    this.dialogVisible.set(true);
    this.preview();
  }

  /** What the options would do. */
  protected preview(): void {
    this.summary.set(null);
    this.teachingApi
      .fillCurriculum(this.overview().versionId, this.options, true)
      .subscribe({
        next: (response) => this.summary.set(response.summary),
        error: (err) => this.notify.actionFailed('fill preview failed', err),
      });
  }

  protected fill(): void {
    this.notify.clear();
    this.busy.set(true);
    this.teachingApi
      .fillCurriculum(this.overview().versionId, this.options, false)
      .subscribe({
        next: (response) => {
          this.busy.set(false);
          this.dialogVisible.set(false);
          if (response.overview) {
            this.changed.emit(response.overview);
          }
          this.notify.success('Učební plán je vyplněný.');
        },
        error: (err) => {
          this.busy.set(false);
          this.notify.actionFailed('fill failed', err);
        },
      });
  }

  protected areaOptions(
    program: RvpProgramCheck,
  ): { label: string; value: string }[] {
    return [
      { label: 'Automaticky', value: AUTOMATIC },
      ...program.areas.map((a) => ({ label: a.name, value: a.key })),
      { label: 'Jen disponibilní hodiny', value: NO_AREA },
    ];
  }

  /** What "automatically" came to, while the school hasn't chosen. */
  protected recognized(program: RvpProgramCheck, subject: RvpSubject): string {
    if (subject.chosen) {
      return '';
    }
    return (
      program.areas.find((a) => a.key === subject.areaKey)?.name ??
      'jen disponibilní hodiny'
    );
  }

  protected setArea(
    program: RvpProgramCheck,
    subject: RvpSubject,
    choice: string,
  ): void {
    const automatic = choice === AUTOMATIC;
    this.busy.set(true);
    this.teachingApi
      .setRvpSubjectArea(
        this.overview().versionId,
        program.code,
        subject.subjectId,
        automatic || choice === NO_AREA ? null : choice,
        automatic,
      )
      .subscribe({
        next: (overview) => {
          this.busy.set(false);
          this.changed.emit(overview);
        },
        error: (err) => {
          this.busy.set(false);
          this.notify.actionFailed('setRvpSubjectArea failed', err);
        },
      });
  }

  protected grades(missing: number[]): string {
    return missing.map((g) => `${g}.`).join(', ');
  }
}

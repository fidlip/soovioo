import {
  Component,
  DestroyRef,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Button } from '@openng/optimus-ui/button';
import { Dialog } from '@openng/optimus-ui/dialog';
import { Message } from '@openng/optimus-ui/message';
import { SelectButton } from '@openng/optimus-ui/selectbutton';
import {
  TimetableGeneration,
  TimetableGenerationMode,
  TimetableVersionsApiService,
} from '@soovioo/shared';
import { Subscription, switchMap, takeWhile, timer } from 'rxjs';
import { Loading } from '../loading/loading';
import { NotifyService } from '../notify.service';

const RUNNING = ['Queued', 'Running'];

/** Generating a draft version's lessons from its učební plán and
 * přidělení výuky; shows the last run's result. */
@Component({
  selector: 'app-timetable-generate-dialog',
  imports: [Button, Dialog, FormsModule, Loading, Message, SelectButton],
  templateUrl: './timetable-generate-dialog.html',
})
export class TimetableGenerateDialog {
  private readonly versionsApi = inject(TimetableVersionsApiService);
  private readonly notify = inject(NotifyService);
  private readonly destroyRef = inject(DestroyRef);

  readonly visible = input(false);
  readonly versionId = input.required<number>();
  readonly closed = output<void>();
  /** Lessons were added to the version. */
  readonly generated = output<void>();

  protected mode: TimetableGenerationMode = 'Fill';
  protected readonly modeOptions = [
    { label: 'Doplnit chybějící', value: 'Fill' },
    { label: 'Přegenerovat vše', value: 'All' },
  ];
  protected readonly generation = signal<TimetableGeneration | null>(null);
  protected readonly loaded = signal(false);
  private polling?: Subscription;

  constructor() {
    effect(() => {
      if (this.visible()) {
        const versionId = this.versionId();
        untracked(() => this.load(versionId));
      }
    });
  }

  protected running(): boolean {
    return RUNNING.includes(this.generation()?.status ?? '');
  }

  private load(versionId: number): void {
    this.loaded.set(false);
    this.generation.set(null);
    this.versionsApi.getGeneration(versionId).subscribe({
      next: (generation) => {
        this.generation.set(generation);
        this.loaded.set(true);
        if (this.running()) {
          this.poll(versionId);
        }
      },
      error: (err) => {
        console.error('getGeneration failed', err);
        this.loaded.set(true);
      },
    });
  }

  protected start(): void {
    const versionId = this.versionId();
    this.versionsApi.generate(versionId, this.mode).subscribe({
      next: (generation) => {
        this.generation.set(generation);
        this.poll(versionId);
      },
      error: (err) => this.notify.actionFailed('generate failed', err),
    });
  }

  private poll(versionId: number): void {
    this.polling?.unsubscribe();
    this.polling = timer(1000, 2000)
      .pipe(
        switchMap(() => this.versionsApi.getGeneration(versionId)),
        takeWhile((g) => g !== null && RUNNING.includes(g.status), true),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (generation) => {
          this.generation.set(generation);
          if (generation?.status === 'Done' && generation.placed > 0) {
            this.generated.emit();
          }
        },
        error: (err) => console.error('getGeneration failed', err),
      });
  }

  protected close(): void {
    this.closed.emit();
  }
}

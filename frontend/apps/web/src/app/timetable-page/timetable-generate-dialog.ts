import {
  Component,
  DestroyRef,
  computed,
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
import { ProgressBar } from '@openng/optimus-ui/progressbar';
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
 * přidělení výuky: the choice, then how the run goes (asked for only while
 * the dialog is open — closed, the bell tells when it's done), then its
 * result. `showResult` opens straight on the last run's result (from that
 * notification). */
@Component({
  selector: 'app-timetable-generate-dialog',
  imports: [
    Button,
    Dialog,
    FormsModule,
    Loading,
    Message,
    ProgressBar,
    SelectButton,
  ],
  templateUrl: './timetable-generate-dialog.html',
})
export class TimetableGenerateDialog {
  private readonly versionsApi = inject(TimetableVersionsApiService);
  private readonly notify = inject(NotifyService);
  private readonly destroyRef = inject(DestroyRef);

  readonly visible = input(false);
  readonly versionId = input.required<number>();
  readonly showResult = input(false);
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
  /** The run whose end is shown. */
  protected readonly result = signal<TimetableGeneration | null>(null);
  /** Ticks with each poll, for the time it's been running. */
  protected readonly now = signal(Date.now());
  private polling?: Subscription;

  protected readonly running = computed(() =>
    RUNNING.includes(this.generation()?.status ?? ''),
  );
  protected readonly header = computed(() =>
    this.result()
      ? 'Výsledek generování'
      : this.running()
        ? 'Generování rozvrhu'
        : 'Generovat rozvrh',
  );

  constructor() {
    effect(() => {
      if (this.visible()) {
        const versionId = this.versionId();
        const showResult = this.showResult();
        untracked(() => this.load(versionId, showResult));
      } else {
        untracked(() => this.stopPolling());
      }
    });
  }

  private load(versionId: number, showResult: boolean): void {
    this.loaded.set(false);
    this.generation.set(null);
    this.result.set(null);
    this.versionsApi.getGeneration(versionId).subscribe({
      next: (generation) => {
        this.generation.set(generation);
        this.loaded.set(true);
        if (this.running()) {
          this.poll(versionId);
        } else if (showResult && generation) {
          this.showEnd(versionId, generation);
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
    this.stopPolling();
    this.polling = timer(1000, 2000)
      .pipe(
        switchMap(() => this.versionsApi.getGeneration(versionId)),
        takeWhile((g) => g !== null && RUNNING.includes(g.status), true),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (generation) => {
          this.now.set(Date.now());
          this.generation.set(generation);
          if (generation && !RUNNING.includes(generation.status)) {
            if (generation.status === 'Done' && generation.placed > 0) {
              this.generated.emit();
            }
            this.showEnd(versionId, generation);
          }
        },
        error: (err) => console.error('getGeneration failed', err),
      });
  }

  /** Seen here, its notification needn't wait under the bell. */
  private showEnd(versionId: number, generation: TimetableGeneration): void {
    this.result.set(generation);
    this.versionsApi.generationSeen(versionId).subscribe({
      error: (err) => console.error('generationSeen failed', err),
    });
  }

  private stopPolling(): void {
    this.polling?.unsubscribe();
    this.polling = undefined;
  }

  protected close(): void {
    this.stopPolling();
    this.closed.emit();
  }

  /** "1 min 20 s". */
  protected span(seconds: number): string {
    const s = Math.max(0, Math.round(seconds));
    const minutes = Math.floor(s / 60);
    return minutes > 0 ? `${minutes} min ${s % 60} s` : `${s} s`;
  }

  protected elapsed(g: TimetableGeneration): string {
    return this.span(
      (this.now() - Date.parse(g.startedAt ?? g.createdAt)) / 1000,
    );
  }

  protected duration(g: TimetableGeneration): string {
    return g.finishedAt
      ? this.span(
          (Date.parse(g.finishedAt) - Date.parse(g.startedAt ?? g.createdAt)) /
            1000,
        )
      : '';
  }
}

import { Component, computed, inject, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Button } from '@openng/optimus-ui/button';
import { DatePicker } from '@openng/optimus-ui/datepicker';
import { Dialog } from '@openng/optimus-ui/dialog';
import { Select } from '@openng/optimus-ui/select';
import { SelectButton } from '@openng/optimus-ui/selectbutton';
import { Textarea } from '@openng/optimus-ui/textarea';
import {
  AbsenceStatus,
  AbsencesApiService,
  AuthApiService,
  EXCUSE_CHANNEL_LABELS,
  Excuse,
  ExcuseChannel,
  ExcuseAttachment,
  ExcuseResult,
  Student,
  StudentReach,
  StudentsApiService,
  formatDate,
  reachesStudent,
  nameText,
} from '@soovioo/shared';
import { concat, last, Observable, of } from 'rxjs';
import { FilePicker } from '../file-picker/file-picker';
import { FloatField, FORM_FIELDS } from '../forms';
import { NotifyService } from '../notify.service';
import { excuseFileError, formatFileSize } from './excuse-files';
import {
  PeriodChips,
  StudentDayPeriods,
  toDateOnlyString,
} from './student-day-periods';

const SKIPPED_LABEL: Partial<Record<AbsenceStatus, string>> = {
  Excused: 'už omluveno',
  Rejected: 'omluvenka zamítnuta',
  PendingExcuse: 'už má omluvenku',
};

/** Writes or edits an omluvenka — one day (whole, or some periods) or a
 * range, the reason and files (a doctor's note). Every school day of it
 * waits for the teacher's approval; a class teacher's own is approved up
 * to today at once. */
@Component({
  selector: 'app-excuse-dialog',
  imports: [
    FloatField,
    ...FORM_FIELDS,
    FormsModule,
    Button,
    DatePicker,
    Dialog,
    FilePicker,
    PeriodChips,
    Select,
    SelectButton,
    Textarea,
  ],
  template: `
    <p-dialog
      [visible]="visible()"
      (visibleChange)="visible.set($event)"
      [header]="editingId === null ? 'Omluvit' : 'Upravit omluvenku'"
      [modal]="true"
      styleClass="w-full max-w-lg"
    >
      <form
        class="flex flex-col gap-3"
        #excuseForm="ngForm"
        (ngSubmit)="save()"
      >
        @if (editingId === null) {
          <app-float-field label="Žák" for="excuseStudent">
            <p-select
              ariaLabel="Žák"
              inputId="excuseStudent"
              [options]="studentOptions()"
              optionLabel="label"
              optionValue="id"
              [(ngModel)]="studentId"
              (ngModelChange)="onDayChange()"
              name="excuseStudent"
              required
              [filter]="studentOptions().length > 8"
              appendTo="body"
              styleClass="w-full"
            />
          </app-float-field>
        }
        <div class="grid grid-cols-2 gap-3">
          <app-float-field label="Od" for="excuseFrom">
            <p-datePicker
              inputId="excuseFrom"
              [(ngModel)]="dateFrom"
              (ngModelChange)="onFromChange()"
              name="dateFrom"
              required
              [showIcon]="true"
              appendTo="body"
            />
          </app-float-field>
          <app-float-field label="Do" for="excuseTo">
            <p-datePicker
              inputId="excuseTo"
              [(ngModel)]="dateTo"
              (ngModelChange)="onDayChange()"
              name="dateTo"
              required
              [showIcon]="true"
              appendTo="body"
            />
          </app-float-field>
        </div>
        <span class="-mt-1 text-xs text-surface-500">Víkendy se přeskočí.</span>

        @if (oneDay() && dayPeriods.periods().length > 0) {
          <p-selectButton
            [options]="extentOptions"
            optionLabel="label"
            optionValue="value"
            [allowEmpty]="false"
            [ngModel]="wholeDay()"
            (ngModelChange)="setWholeDay($event)"
            name="extent"
            ariaLabel="Rozsah"
          />
          @if (!wholeDay()) {
            <app-period-chips
              [periods]="dayPeriods.periods()"
              [(selected)]="periods"
            />
            @if (periods().length === 0) {
              <span class="text-xs text-surface-500"
                >Bez vybraných hodin se omluví celý den.</span
              >
            }
          }
        }

        @if (asksChannel()) {
          <app-float-field label="Způsob předání" for="excuseChannel">
            <p-select
              ariaLabel="Způsob předání"
              inputId="excuseChannel"
              [options]="channelOptions"
              optionLabel="label"
              optionValue="value"
              [(ngModel)]="channel"
              name="channel"
              required
              appendTo="body"
              styleClass="w-full"
            />
          </app-float-field>
        }

        <app-float-field label="Důvod" for="excuseReason">
          <textarea
            id="excuseReason"
            pTextarea
            rows="2"
            name="reason"
            required
            maxlength="500"
            [(ngModel)]="reason"
          ></textarea>
        </app-float-field>

        <div class="flex flex-col gap-1.5">
          <span
            class="text-sm font-medium text-surface-600 dark:text-surface-300"
            >Přílohy (PDF nebo obrázek, max 10 MB)</span
          >
          @for (file of kept(); track file.id) {
            <div class="flex items-center gap-2 text-sm">
              <i class="pi pi-paperclip text-surface-500"></i>
              <span class="min-w-0 truncate"
                >{{ file.fileName }} ({{ fileSize(file.sizeBytes) }})</span
              >
              <p-button
                type="button"
                icon="pi pi-times"
                severity="secondary"
                [text]="true"
                [rounded]="true"
                size="small"
                [ariaLabel]="'Odebrat ' + file.fileName"
                (onClick)="removeKept(file)"
              />
            </div>
          }
          @for (file of added(); track $index) {
            <div class="flex items-center gap-2 text-sm">
              <i class="pi pi-paperclip text-surface-500"></i>
              <span class="min-w-0 truncate"
                >{{ file.name }} ({{ fileSize(file.size) }})</span
              >
              <p-button
                type="button"
                icon="pi pi-times"
                severity="secondary"
                [text]="true"
                [rounded]="true"
                size="small"
                [ariaLabel]="'Odebrat ' + file.name"
                (onClick)="removeAdded($index)"
              />
            </div>
          }
          @if (visible()) {
            <app-file-picker
              inputId="excuseFile"
              label="Přiložit soubor"
              (fileSelected)="onFileSelected($event)"
            />
          }
          @if (fileError(); as error) {
            <p class="m-0 text-sm text-red-500">{{ error }}</p>
          }
        </div>

        <app-submit-button
          [form]="excuseForm"
          [label]="editingId === null ? 'Omluvit' : 'Uložit'"
          icon="pi pi-check"
          class="self-start"
        />
      </form>
    </p-dialog>
  `,
})
export class ExcuseDialog {
  private readonly absencesApi = inject(AbsencesApiService);
  private readonly studentsApi = inject(StudentsApiService);
  private readonly authApi = inject(AuthApiService);
  private readonly notify = inject(NotifyService);
  protected readonly dayPeriods = new StudentDayPeriods();

  /** After an omluvenka is saved. */
  readonly saved = output<void>();

  protected readonly visible = signal(false);
  private readonly students = signal<Student[] | null>(null);
  /** Whom AbsencesExcuse reaches — a parent's children, a class teacher's class. */
  private readonly reach = signal<StudentReach | null>(null);
  protected readonly studentOptions = computed(() =>
    (this.students() ?? [])
      .filter((s) => reachesStudent(this.reach(), s.id))
      .map((s) => ({
        id: s.id,
        label: nameText(`${s.firstName} ${s.lastName}`, s.label),
      })),
  );

  protected readonly channelOptions = Object.entries(EXCUSE_CHANNEL_LABELS).map(
    ([value, label]) => ({ value: value as ExcuseChannel, label }),
  );
  /** How the family gave it — asked of staff only. */
  protected channel: ExcuseChannel | null = null;

  protected readonly extentOptions = [
    { label: 'Celý den', value: true },
    { label: 'Jen některé hodiny', value: false },
  ];

  /** null = a new omluvenka. */
  protected editingId: number | null = null;
  protected studentId: number | null = null;
  protected dateFrom = new Date();
  protected dateTo = new Date();
  /** Od before its last change. */
  private shownFrom: Date | null = null;
  protected reason = '';
  protected readonly wholeDay = signal(true);
  protected readonly periods = signal<number[]>([]);
  protected readonly oneDay = signal(true);
  /** The edited omluvenka's files that stay. */
  protected readonly kept = signal<ExcuseAttachment[]>([]);
  private removed: ExcuseAttachment[] = [];
  protected readonly added = signal<File[]>([]);
  protected readonly fileError = signal<string | null>(null);
  protected readonly fileSize = formatFileSize;

  /** A new omluvenka (for the pupil and day of an absence, if given), or
   * editing `excuse`. */
  open(
    options: { excuse?: Excuse; studentId?: number; date?: string } = {},
  ): void {
    const { excuse } = options;
    this.editingId = excuse?.id ?? null;
    const from = excuse?.dateFrom ?? options.date;
    const to = excuse?.dateTo ?? options.date;
    this.dateFrom = from ? this.parse(from) : this.today();
    this.dateTo = to ? this.parse(to) : this.today();
    this.shownFrom = this.dateFrom;
    this.reason = excuse?.reason ?? '';
    this.channel = excuse?.channel ?? null;
    this.withChannel = !excuse || excuse.channel !== null;
    this.periods.set(excuse ? [...excuse.periods] : []);
    this.wholeDay.set(!excuse || excuse.periods.length === 0);
    this.kept.set(excuse ? [...excuse.attachments] : []);
    this.removed = [];
    this.added.set([]);
    this.fileError.set(null);
    this.notify.clear();
    this.visible.set(true);
    const studentId = excuse?.studentId ?? options.studentId;
    this.studentId = studentId ?? null;
    if (this.students() === null) {
      this.studentsApi.getStudents().subscribe({
        next: (students) => {
          this.students.set(students);
          this.preselectStudent(studentId);
          this.onDayChange(false);
        },
        error: (err) => console.error('getStudents failed', err),
      });
      this.studentsApi.getReach('AbsencesExcuse').subscribe({
        next: (reach) => this.reach.set(reach),
        error: (err) => console.error('getReach failed', err),
      });
    } else {
      this.preselectStudent(studentId);
    }
    this.onDayChange(false);
  }

  /** Do follows Od while it's the same day, or when Od passes it. */
  protected onFromChange(): void {
    // Half-typed dates come as null — Do waits for a whole one.
    if (!this.dateFrom) {
      return;
    }
    const sameDay =
      !!this.dateTo &&
      !!this.shownFrom &&
      toDateOnlyString(this.dateTo) === toDateOnlyString(this.shownFrom);
    if (
      this.dateFrom &&
      (!this.dateTo || sameDay || this.dateTo < this.dateFrom)
    ) {
      this.dateTo = this.dateFrom;
    }
    this.shownFrom = this.dateFrom;
    this.onDayChange();
  }

  /** Pupil or dates changed: a new day starts as the whole day. */
  protected onDayChange(reset = true): void {
    const one =
      !!this.dateFrom &&
      !!this.dateTo &&
      toDateOnlyString(this.dateFrom) === toDateOnlyString(this.dateTo);
    this.oneDay.set(one);
    if (reset) {
      this.wholeDay.set(true);
      this.periods.set([]);
    }
    if (one) {
      this.dayPeriods.show(
        this.students()?.find((s) => s.id === this.studentId),
        this.dateFrom,
      );
    }
  }

  protected setWholeDay(whole: boolean): void {
    this.wholeDay.set(whole);
    this.periods.set(whole ? [] : this.dayPeriods.lessonPeriods());
  }

  protected onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) {
      return;
    }
    const error = excuseFileError(file);
    this.fileError.set(error);
    if (!error) {
      this.added.update((files) => [...files, file]);
    }
  }

  protected removeKept(file: ExcuseAttachment): void {
    this.kept.update((files) => files.filter((f) => f.id !== file.id));
    this.removed.push(file);
  }

  protected removeAdded(index: number): void {
    this.added.update((files) => files.filter((_, i) => i !== index));
  }

  protected save(): void {
    if (!this.studentId) {
      return;
    }
    const request = {
      studentId: this.studentId,
      dateFrom: toDateOnlyString(this.dateFrom),
      dateTo: toDateOnlyString(this.dateTo),
      reason: this.reason.trim(),
      periods: this.oneDay() && !this.wholeDay() ? this.periods() : [],
      channel: this.asksChannel() ? this.channel : null,
    };
    const result =
      this.editingId === null
        ? this.absencesApi.createExcuse(request)
        : this.absencesApi.updateExcuse(this.editingId, request);
    result.subscribe({
      next: (result) => {
        const done = () => {
          this.visible.set(false);
          this.tell(result);
          this.saved.emit();
        };
        if (result.excuseId === null) {
          done();
          return;
        }
        this.saveFiles(result.excuseId).subscribe({
          next: done,
          error: (err) => {
            this.notify.actionFailed('excuse attachments failed', err);
            this.saved.emit();
          },
        });
      },
      error: (err) => this.notify.actionFailed('saveExcuse failed', err),
    });
  }

  /** Removed files go, added ones are uploaded — one after another. */
  private saveFiles(excuseId: number): Observable<unknown> {
    const steps: Observable<unknown>[] = [
      ...this.removed.map((f) =>
        this.absencesApi.deleteExcuseAttachment(excuseId, f.id),
      ),
      ...this.added().map((f) =>
        this.absencesApi.uploadExcuseAttachment(excuseId, f),
      ),
    ];
    return steps.length === 0 ? of(null) : concat(...steps).pipe(last());
  }

  private tell(result: ExcuseResult): void {
    if (result.skipped.length === 0) {
      this.notify.success(
        this.editingId !== null
          ? 'Omluvenka je upravená.'
          : this.isFamily()
            ? 'Omluvenka je odeslaná, čeká na schválení.'
            : 'Omluvenka je zapsaná.',
      );
      return;
    }
    const days = result.skipped
      .map(
        (d) =>
          `${formatDate(d.date, 'date')} (${SKIPPED_LABEL[d.status] ?? 'už zapsáno'})`,
      )
      .join(', ');
    this.notify.warn(
      result.excuseId === null
        ? `Omluvenka už nemá žádný den. Vynechané dny: ${days}.`
        : `Omluveno, kromě dnů: ${days}.`,
    );
  }

  /** A parent; staff write down what the family gave them. */
  /** A new or staff-written omluvenka: staff say how it came. */
  private withChannel = true;

  protected asksChannel(): boolean {
    return !this.isFamily() && this.withChannel;
  }

  protected isFamily(): boolean {
    return this.authApi.scope('AbsencesExcuse') === 'Own';
  }

  private preselectStudent(studentId?: number): void {
    if (studentId !== undefined) {
      this.studentId = studentId;
      return;
    }
    const options = this.studentOptions();
    if (!options.some((s) => s.id === this.studentId)) {
      this.studentId = options.length === 1 ? options[0].id : null;
    }
  }

  private today(): Date {
    const now = new Date();
    return new Date(now.getFullYear(), now.getMonth(), now.getDate());
  }

  private parse(date: string): Date {
    return new Date(`${date}T00:00:00`);
  }
}

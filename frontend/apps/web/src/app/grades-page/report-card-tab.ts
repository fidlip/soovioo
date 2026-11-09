import {
  Component,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ConfirmationService } from '@openng/optimus-ui/api';
import { Button } from '@openng/optimus-ui/button';
import { Checkbox } from '@openng/optimus-ui/checkbox';
import { Select } from '@openng/optimus-ui/select';
import { Subscription } from 'rxjs';
import {
  AuthApiService,
  BehaviorClassificationValue,
  DateFormatPipe,
  Gender,
  HalfYearKey,
  ReportCard,
  ReportCardApiService,
  ReportCardClasses,
  ReportCardGradeKind,
  ReportCardResult,
  ReportCardSubject,
  Student,
  Subject,
} from '@soovioo/shared';
import { ClearFilters, FloatField } from '../forms';
import { Loading } from '../loading/loading';
import { NotifyService } from '../notify.service';
import { TermPipe } from '../term.pipe';

/** The half-year a day belongs to: 1. pololetí 1 Sep – 31 Jan, 2. pololetí
 * 1 Feb – 31 Aug (fixed until the school has its own school year, #253). */
export function halfYearOf(day: Date): HalfYearKey {
  const month = day.getMonth() + 1;
  const year = day.getFullYear();
  if (month >= 9) {
    return { schoolYearStartYear: year, halfYear: 'First' };
  }
  return month === 1
    ? { schoolYearStartYear: year - 1, halfYear: 'First' }
    : { schoolYearStartYear: year - 1, halfYear: 'Second' };
}

/** "Všechny moje třídy" — for staff who don't see the whole school. */
const ALL_CLASSES = 0;

const GRADE_WORDS = [
  'výborný',
  'chvalitebný',
  'dobrý',
  'dostatečný',
  'nedostatečný',
];

const MARK_OPTIONS = [
  ...GRADE_WORDS.map((word, i) => ({
    value: `${i + 1}`,
    label: `${i + 1} – ${word}`,
  })),
  { value: 'Exempt', label: 'uvolněn/a' },
  { value: 'NotGraded', label: 'nehodnocen/a' },
];

const BEHAVIOR: Record<BehaviorClassificationValue, string> = {
  VelmiDobre: 'velmi dobré',
  Uspokojive: 'uspokojivé',
  Neuspokojive: 'neuspokojivé',
};

/** "prospěl" / "prospěla" / "prospěl/a" by the pupil's gender. */
function gendered(base: string, gender: Gender | null): string {
  return gender === 'Female'
    ? `${base}a`
    : gender === 'Male'
      ? base
      : `${base}/a`;
}

/** Vysvědčení in Známky: a half-year's report cards — of a class for staff,
 * of the family's children for them. A report card grade is suggested from
 * the half-year's average; the teacher may change it and confirms it, a
 * confirmed one only the management reopens. */
@Component({
  selector: 'app-report-card-tab',
  imports: [
    ClearFilters,
    DateFormatPipe,
    FloatField,
    FormsModule,
    Button,
    Checkbox,
    Loading,
    Select,
    TermPipe,
  ],
  templateUrl: './report-card-tab.html',
})
export class ReportCardTab {
  private readonly api = inject(ReportCardApiService);
  private readonly authApi = inject(AuthApiService);
  private readonly confirmation = inject(ConfirmationService);
  private readonly notify = inject(NotifyService);

  readonly students = input.required<Student[]>();
  readonly subjects = input.required<Subject[]>();
  /** The pupil's grades in the subject, from its grade count. */
  readonly showGrades = output<{ studentId: number; subjectId: number }>();

  protected readonly isStaff = computed(() => this.authApi.isStaff());
  protected readonly markOptions = MARK_OPTIONS;

  /** The current school year's halves and the previous one's, newest first. */
  protected readonly halfYearOptions = (() => {
    const current = halfYearOf(new Date());
    const options: { key: string; label: string; value: HalfYearKey }[] = [];
    for (const year of [
      current.schoolYearStartYear,
      current.schoolYearStartYear - 1,
    ]) {
      for (const halfYear of ['Second', 'First'] as const) {
        if (
          year === current.schoolYearStartYear &&
          halfYear === 'Second' &&
          current.halfYear === 'First'
        ) {
          continue;
        }
        options.push({
          key: `${year}-${halfYear}`,
          label: `${year}/${(year + 1) % 100} – ${halfYear === 'First' ? 1 : 2}. pololetí`,
          value: { schoolYearStartYear: year, halfYear },
        });
      }
    }
    return options;
  })();

  protected readonly halfYearKey = signal(this.halfYearOptions[0].key);
  private readonly halfYear = computed(
    () => this.halfYearOptions.find((o) => o.key === this.halfYearKey())!.value,
  );
  protected readonly classId = signal<number | null>(null);
  protected readonly subjectId = signal<number | null>(null);
  protected readonly studentId = signal<number | null>(null);

  protected readonly onlyMine = signal(false);
  protected readonly onlyUnconfirmed = signal(false);
  /** A single class picked — its report cards go into one PDF. */
  protected readonly pickedClassId = computed(() => {
    const classId = this.classId();
    return classId === ALL_CLASSES ? null : classId;
  });

  /** Staff: the classes whose report cards they see. */
  private readonly classes = signal<ReportCardClasses | null>(null);
  protected readonly classOptions = computed(() => {
    const reach = this.classes();
    if (!reach) {
      return [];
    }
    const options = [...reach.classes]
      .sort((a, b) => a.name.localeCompare(b.name, 'cs', { numeric: true }))
      .map((c) => ({ id: c.id, label: c.own ? `${c.name} (moje)` : c.name }));
    return !reach.all && options.length > 1
      ? [{ id: ALL_CLASSES, label: 'Všechny moje třídy' }, ...options]
      : options;
  });
  protected readonly subjectOptions = computed(() =>
    this.subjects().filter((s) => !s.coveredByDivisions),
  );
  protected readonly studentOptions = computed(() => {
    const classId = this.classId();
    const classIds = new Set(this.classOptions().map((c) => c.id));
    return this.students()
      .filter(
        (s) =>
          !this.isStaff() ||
          (classId === ALL_CLASSES
            ? classIds.has(s.schoolClassId)
            : s.schoolClassId === classId),
      )
      .map((s) => ({ id: s.id, label: `${s.firstName} ${s.lastName}` }));
  });

  protected readonly cards = signal<ReportCard[] | null>(null);
  private request?: Subscription;

  /** "Mé" — the subjects the caller grades; "Nepotvrzené" — not confirmed
   * yet; both — what's left for the caller to confirm. Only the pupils with
   * any. */
  protected readonly shownCards = computed(() => {
    const cards = this.cards();
    const mine = this.onlyMine();
    const unconfirmed = this.onlyUnconfirmed();
    if (!cards || (!mine && !unconfirmed)) {
      return cards;
    }
    return cards
      .map((card) => ({
        ...card,
        subjects: card.subjects.filter(
          (s) => (!mine || s.mine) && (!unconfirmed || !s.confirmed),
        ),
      }))
      .filter((card) => card.subjects.length > 0);
  });

  /** Unconfirmed grades shown that the caller may confirm — with a value. */
  protected readonly confirmable = computed(() =>
    (this.shownCards() ?? []).flatMap((card) =>
      card.subjects
        .filter((s) => s.canEdit && s.kind !== null)
        .map((s) => ({ studentId: card.studentId, subjectId: s.subjectId })),
    ),
  );

  /** Families see report card grades once the report card is issued. */
  protected readonly notIssuedOn = computed(() => {
    const card = this.cards()?.[0];
    return !this.isStaff() && card && !card.issued ? card.issuedOn : null;
  });

  constructor() {
    if (this.isStaff()) {
      // Their own class (třídní) first.
      this.api.getClasses().subscribe({
        next: (reach) => {
          this.classes.set(reach);
          const own = reach.classes.find((c) => c.own);
          const first = this.classOptions().find((c) => c.id !== ALL_CLASSES);
          this.classId.set(own?.id ?? first?.id ?? null);
        },
        error: (err) => {
          console.error('getClasses failed', err);
          this.classes.set({ all: false, classes: [] });
        },
      });
    }
    effect(() => {
      this.halfYear();
      this.classId();
      this.subjectId();
      this.studentId();
      this.reload();
    });
  }

  protected onClassChange(classId: number): void {
    this.classId.set(classId);
    this.studentId.set(null);
  }

  protected clearFilters(): void {
    this.subjectId.set(null);
    this.studentId.set(null);
    this.onlyMine.set(false);
    this.onlyUnconfirmed.set(false);
  }

  protected markKey(subject: ReportCardSubject): string | null {
    if (subject.kind === null) {
      return null;
    }
    return subject.kind === 'Graded' ? `${subject.value}` : subject.kind;
  }

  protected markText(
    subject: ReportCardSubject,
    gender: Gender | null,
  ): string {
    switch (subject.kind) {
      case null:
        return '—';
      case 'Exempt':
        return gendered('uvolněn', gender);
      case 'NotGraded':
        return gendered('nehodnocen', gender);
      default:
        return `${subject.value} – ${GRADE_WORDS[(subject.value ?? 1) - 1]}`;
    }
  }

  protected resultText(card: ReportCard): string {
    const labels: Record<ReportCardResult, string> = {
      WithHonours: `${gendered('prospěl', card.gender)} s vyznamenáním`,
      Passed: gendered('prospěl', card.gender),
      Failed: gendered('neprospěl', card.gender),
      NotAssessed: gendered('nehodnocen', card.gender),
    };
    return card.result ? labels[card.result] : 'neuzavřeno';
  }

  protected behaviorText(card: ReportCard): string {
    return BEHAVIOR[card.behavior];
  }

  protected formatAverage(average: number): string {
    return average.toFixed(2).replace('.', ',');
  }

  protected setMark(
    card: ReportCard,
    subject: ReportCardSubject,
    key: string | null,
  ): void {
    if (key === null) {
      this.reset(card, subject);
      return;
    }
    const kind: ReportCardGradeKind =
      key === 'Exempt' || key === 'NotGraded' ? key : 'Graded';
    this.api
      .setGrade(
        this.halfYear(),
        this.key(card, subject),
        kind,
        kind === 'Graded' ? Number(key) : null,
      )
      .subscribe({
        next: () => this.reload(),
        error: (err) => this.failed('setGrade failed', err),
      });
  }

  protected reset(card: ReportCard, subject: ReportCardSubject): void {
    this.api.resetGrade(this.halfYear(), this.key(card, subject)).subscribe({
      next: () => this.reload(),
      error: (err) => this.failed('resetGrade failed', err),
    });
  }

  protected confirmOne(card: ReportCard, subject: ReportCardSubject): void {
    this.confirm([this.key(card, subject)]);
  }

  protected confirmShown(): void {
    const keys = this.confirmable();
    this.confirmation.confirm({
      header: 'Potvrdit známky na vysvědčení',
      message: `Potvrdit zobrazené známky na vysvědčení (počet: ${keys.length})? Potvrzenou známku může změnit už jen vedení školy.`,
      icon: 'pi pi-check-circle',
      acceptLabel: 'Potvrdit',
      rejectLabel: 'Zrušit',
      rejectButtonProps: { severity: 'secondary', outlined: true },
      accept: () => this.confirm(keys),
    });
  }

  protected reopen(card: ReportCard, subject: ReportCardSubject): void {
    this.api.reopenGrade(this.halfYear(), this.key(card, subject)).subscribe({
      next: () => this.reload(),
      error: (err) => this.failed('reopenGrade failed', err),
    });
  }

  protected downloadPdf(card: ReportCard): void {
    this.download({ studentId: card.studentId }, card.studentName);
  }

  protected downloadClassPdf(): void {
    const classId = this.classId();
    const name = this.classes()?.classes.find((c) => c.id === classId)?.name;
    if (classId !== null && name) {
      this.download({ schoolClassId: classId }, name);
    }
  }

  private confirm(keys: { studentId: number; subjectId: number }[]): void {
    this.api.confirmGrades(this.halfYear(), keys).subscribe({
      next: (result) => {
        if (result.skipped > 0) {
          this.notify.warn(
            `Bez známky na vysvědčení nejde potvrdit (počet: ${result.skipped}) — vyberte ji nebo nehodnocen/a.`,
          );
        }
        this.reload();
      },
      error: (err) => this.failed('confirmGrades failed', err),
    });
  }

  private download(
    of: { studentId: number } | { schoolClassId: number },
    name: string,
  ): void {
    this.api.downloadPdf(this.halfYear(), of).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = `${this.pdfKind()}-${name}.pdf`;
        link.click();
        URL.revokeObjectURL(url);
      },
      error: (err) => this.failed('downloadPdf failed', err),
    });
  }

  /** 1. pololetí: výpis z vysvědčení; 2. pololetí: the particulars for
   * the official form (no výpis then). */
  private pdfKind(): string {
    return this.halfYear().halfYear === 'First'
      ? 'vypis-z-vysvedceni'
      : 'podklady-k-vysvedceni';
  }

  private key(card: ReportCard, subject: ReportCardSubject) {
    return { studentId: card.studentId, subjectId: subject.subjectId };
  }

  private failed(context: string, err: unknown): void {
    this.notify.actionFailed(context, err);
    this.reload();
  }

  private reload(): void {
    const classId = this.classId();
    if (this.isStaff() && classId === null) {
      this.cards.set(this.classes() === null ? null : []);
      return;
    }
    this.request?.unsubscribe();
    this.request = this.api
      .getReportCards({
        ...this.halfYear(),
        schoolClassId: classId === ALL_CLASSES ? null : classId,
        studentId: this.studentId(),
        subjectId: this.subjectId(),
      })
      .subscribe({
        next: (cards) => this.cards.set(cards),
        error: (err) => {
          console.error('getReportCards failed', err);
          this.cards.set([]);
        },
      });
  }
}

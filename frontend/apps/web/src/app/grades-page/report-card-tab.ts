import {
  Component,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ConfirmationService } from '@openng/optimus-ui/api';
import { Button } from '@openng/optimus-ui/button';
import { Checkbox } from '@openng/optimus-ui/checkbox';
import { Select } from '@openng/optimus-ui/select';
import { Subscription } from 'rxjs';
import {
  AssessmentMode,
  AuthApiService,
  BehaviorClassificationValue,
  DateFormatPipe,
  Gender,
  ReportCard,
  ReportCardApiService,
  ReportCardClasses,
  ReportCardGradeKind,
  ReportCardPrint,
  ReportCardResult,
  ReportCardSubject,
  SchoolYearsStore,
  Student,
  Subject,
} from '@soovioo/shared';
import { todayString } from '../absences-page/excuse-days';
import { ClearFilters, FloatField } from '../forms';
import { Loading } from '@fidlip/soovioo-ui';
import { NotifyService } from '@fidlip/soovioo-ui';
import { TermPipe } from '../term.pipe';
import { VerbalTextDialog } from './verbal-text-dialog';

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

/** Under slovní hodnocení the stupeň it corresponds to. */
const VERBAL_MARK_OPTIONS = MARK_OPTIONS.map((o) => ({
  ...o,
  label: /^\d/.test(o.value) ? `odpovídá ${o.label}` : o.label,
}));

const MODE_LABELS: Record<AssessmentMode, string> = {
  Classification: 'klasifikace',
  Verbal: 'slovní hodnocení',
  ClassificationWithVerbal: 'klasifikace a slovní hodnocení',
};

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

/** Vysvědčení in Známky: a grading period's report cards — of a class for staff,
 * of the family's children for them. A report card grade is suggested from
 * the period's average; the teacher may change it and confirms it, a
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
    VerbalTextDialog,
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
  protected readonly verbalMarkOptions = VERBAL_MARK_OPTIONS;
  protected readonly modeLabels = MODE_LABELS;
  private readonly textDialog = viewChild.required(VerbalTextDialog);

  private readonly schoolYears = inject(SchoolYearsStore);

  /** The periods with a report card that have started, newest first. */
  protected readonly periodOptions = computed(() => {
    const today = todayString();
    return this.schoolYears
      .reportPeriods()
      .filter((o) => o.period.from <= today);
  });

  /** Picked by hand; until then the period today is in. */
  private readonly pickedPeriodId = signal<number | null>(null);
  protected readonly periodId = computed(() => {
    const options = this.periodOptions();
    const picked = this.pickedPeriodId();
    if (options.some((o) => o.id === picked)) {
      return picked;
    }
    const today = todayString();
    return (
      options.find((o) => o.period.to >= today)?.id ?? options[0]?.id ?? null
    );
  });
  private readonly period = computed(() =>
    this.periodOptions().find((o) => o.id === this.periodId()),
  );
  /** For the API; only asked for with a period picked. */
  private key(): { gradingPeriodId: number } {
    return { gradingPeriodId: this.periodId() ?? 0 };
  }
  protected readonly classId = signal<number | null>(null);
  protected readonly subjectId = signal<number | null>(null);
  protected readonly studentId = signal<number | null>(null);

  protected readonly onlyMine = signal(false);
  protected readonly onlyUnconfirmed = signal(false);
  /** A single class picked whose report cards the caller may print (their
   * own, or any for who sees the whole school) — into one PDF. */
  protected readonly printableClassId = computed(() => {
    const classId = this.classId();
    const reach = this.classes();
    const printable =
      reach?.all || reach?.classes.some((c) => c.id === classId && c.own);
    return classId === ALL_CLASSES || !printable ? null : classId;
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
        .filter((s) => s.canEdit && s.complete)
        .map((s) => ({ studentId: card.studentId, subjectId: s.subjectId })),
    ),
  );

  /** Any pupil shown is assessed slovně — lists A/B for the class. */
  protected readonly classHasVerbal = computed(() =>
    (this.cards() ?? []).some((card) => card.hasVerbal),
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
    this.schoolYears.ensure();
    effect(() => {
      this.periodId();
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
        if (subject.value === null) {
          return gendered('hodnocen', gender) + ' slovně';
        }
        return `${subject.value} – ${GRADE_WORDS[(subject.value ?? 1) - 1]}`;
    }
  }

  protected resultText(card: ReportCard): string {
    return card.result
      ? this.resultLabel(card.result, card.gender)
      : 'neuzavřeno';
  }

  protected behaviorText(card: ReportCard): string {
    if (card.behavior === null) {
      return 'slovní hodnocení';
    }
    return card.behaviorMode === 'Verbal'
      ? `slovní hodnocení (odpovídá: ${BEHAVIOR[card.behavior]})`
      : BEHAVIOR[card.behavior];
  }

  /** Where the subject's grade stands until it's confirmed. */
  protected status(subject: ReportCardSubject): string {
    if (subject.kind && !subject.complete) {
      return 'chybí slovní hodnocení';
    }
    return subject.changed
      ? 'upraveno'
      : subject.kind
        ? 'návrh z průměru'
        : 'chybí';
  }

  protected editText(card: ReportCard, subject: ReportCardSubject): void {
    this.textDialog().open({
      header: `${subject.subjectName} – ${card.studentName}`,
      text: subject.verbalText,
      save: (text) =>
        this.api
          .setGradeText(this.key(), this.gradeKey(card, subject), text)
          .subscribe({
            next: () => this.reload(),
            error: (err) => this.failed('setGradeText failed', err),
          }),
    });
  }

  protected editBehavior(card: ReportCard): void {
    this.textDialog().open({
      header: `Chování – ${card.studentName}`,
      text: card.behaviorText,
      save: (text) =>
        this.api.setBehaviorText(this.key(), card.studentId, text).subscribe({
          next: () => this.reload(),
          error: (err) => this.failed('setBehaviorText failed', err),
        }),
    });
  }

  protected editSummary(card: ReportCard): void {
    const results: ReportCardResult[] = [
      'WithHonours',
      'Passed',
      'Failed',
      'NotAssessed',
    ];
    this.textDialog().open({
      header: `Souhrnné hodnocení – ${card.studentName}`,
      text: card.summary?.text ?? null,
      results: results.map((value) => ({
        value,
        label: this.resultLabel(value, card.gender),
      })),
      result: card.summary?.result ?? null,
      save: (text, result) =>
        this.api
          .setSummary(this.key(), card.studentId, text, result)
          .subscribe({
            next: () => this.reload(),
            error: (err) => this.failed('setSummary failed', err),
          }),
    });
  }

  protected confirmSummary(card: ReportCard): void {
    this.confirmation.confirm({
      header: 'Potvrdit souhrnné hodnocení',
      message: `Potvrdit souhrnné hodnocení – ${card.studentName}? Potvrzené může změnit už jen vedení školy.`,
      icon: 'pi pi-check-circle',
      acceptLabel: 'Potvrdit',
      rejectLabel: 'Zrušit',
      rejectButtonProps: { severity: 'secondary', outlined: true },
      accept: () =>
        this.api.confirmSummary(this.key(), card.studentId).subscribe({
          next: () => this.reload(),
          error: (err) => this.failed('confirmSummary failed', err),
        }),
    });
  }

  protected reopenSummary(card: ReportCard): void {
    this.api.reopenSummary(this.key(), card.studentId).subscribe({
      next: () => this.reload(),
      error: (err) => this.failed('reopenSummary failed', err),
    });
  }

  protected resultLabel(
    result: ReportCardResult,
    gender: Gender | null,
  ): string {
    const labels: Record<ReportCardResult, string> = {
      WithHonours: `${gendered('prospěl', gender)} s vyznamenáním`,
      Passed: gendered('prospěl', gender),
      Failed: gendered('neprospěl', gender),
      NotAssessed: gendered('nehodnocen', gender),
    };
    return labels[result];
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
        this.key(),
        this.gradeKey(card, subject),
        kind,
        kind === 'Graded' ? Number(key) : null,
      )
      .subscribe({
        next: () => this.reload(),
        error: (err) => this.failed('setGrade failed', err),
      });
  }

  protected reset(card: ReportCard, subject: ReportCardSubject): void {
    this.api.resetGrade(this.key(), this.gradeKey(card, subject)).subscribe({
      next: () => this.reload(),
      error: (err) => this.failed('resetGrade failed', err),
    });
  }

  protected confirmOne(card: ReportCard, subject: ReportCardSubject): void {
    this.confirm([this.gradeKey(card, subject)]);
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
    this.api.reopenGrade(this.key(), this.gradeKey(card, subject)).subscribe({
      next: () => this.reload(),
      error: (err) => this.failed('reopenGrade failed', err),
    });
  }

  protected downloadPdf(
    card: ReportCard,
    form: ReportCardPrint = 'Extract',
  ): void {
    this.download({ studentId: card.studentId }, card.studentName, form);
  }

  protected downloadClassPdf(form: ReportCardPrint = 'Extract'): void {
    const classId = this.classId();
    const name = this.classes()?.classes.find((c) => c.id === classId)?.name;
    if (classId !== null && name) {
      this.download({ schoolClassId: classId }, name, form);
    }
  }

  private confirm(keys: { studentId: number; subjectId: number }[]): void {
    this.api.confirmGrades(this.key(), keys).subscribe({
      next: (result) => {
        if (result.skipped > 0) {
          this.notify.warn(
            `Bez známky na vysvědčení nebo slovního hodnocení nejde potvrdit (počet: ${result.skipped}) — doplňte je, nebo vyberte nehodnocen/a.`,
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
    form: ReportCardPrint,
  ): void {
    this.api.downloadPdf(this.key(), of, form).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = `${form === 'Verbal' ? 'vysvedceni-list-a-b' : this.pdfKind()}-${name}.pdf`;
        link.click();
        URL.revokeObjectURL(url);
      },
      error: (err) => this.failed('downloadPdf failed', err),
    });
  }

  /** 1. pololetí: výpis z vysvědčení; 2. pololetí: the particulars for
   * the official form (no výpis then). */
  private pdfKind(): string {
    return this.period()?.period.final
      ? 'podklady-k-vysvedceni'
      : 'vypis-z-vysvedceni';
  }

  private gradeKey(card: ReportCard, subject: ReportCardSubject) {
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
    if (this.periodId() === null) {
      this.request?.unsubscribe();
      this.cards.set(this.schoolYears.years() === null ? null : []);
      return;
    }
    this.request?.unsubscribe();
    this.request = this.api
      .getReportCards({
        ...this.key(),
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

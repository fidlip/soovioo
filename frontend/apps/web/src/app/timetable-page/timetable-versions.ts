import {
  Component,
  computed,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Button } from '@openng/optimus-ui/button';
import { ConfirmationService } from '@openng/optimus-ui/api';
import { DatePicker } from '@openng/optimus-ui/datepicker';
import { Dialog } from '@openng/optimus-ui/dialog';
import { InputText } from '@openng/optimus-ui/inputtext';
import { Message } from '@openng/optimus-ui/message';
import { Popover } from '@openng/optimus-ui/popover';
import { Select } from '@openng/optimus-ui/select';
import { Tag } from '@openng/optimus-ui/tag';
import {
  SubstitutionsApiService,
  TimetableVersion,
  TimetableVersionsApiService,
  formatDate,
  timetableVersionOn,
} from '@soovioo/shared';
import { Observable } from 'rxjs';
import { toDateOnly } from '../departures/retention-labels';
import { FORM_FIELDS, FloatField } from '../forms';
import { Loading } from '../loading/loading';
import { NotifyService } from '../notify.service';
import { TimetableVersionSelection } from './timetable-version-selection';

type Severity = 'success' | 'info' | 'warn' | 'secondary';

/** "Koncept", or for a published version whether it applies now. */
export function versionStatusLabel(
  version: TimetableVersion,
  today: string,
): { label: string; severity: Severity } {
  switch (version.status) {
    case 'Draft':
      return { label: 'Koncept', severity: 'warn' };
    case 'Archived':
      return { label: 'Archivovaná', severity: 'secondary' };
  }
  if (version.validFrom > today) {
    return { label: 'Naplánovaná', severity: 'info' };
  }
  if (version.validTo !== null && version.validTo < today) {
    return { label: 'Platila', severity: 'secondary' };
  }
  return { label: 'Platí', severity: 'success' };
}

export function versionValidity(version: TimetableVersion): string {
  const from = formatDate(version.validFrom, 'date');
  if (version.status !== 'Published') {
    return `od ${from}`;
  }
  return version.validTo
    ? `${from} – ${formatDate(version.validTo, 'date')}`
    : `od ${from}`;
}

const NOTIFY_OPTIONS: { label: string; value: number | null }[] = [
  { label: 'Dnes večer', value: null },
  { label: 'Den před začátkem platnosti', value: 1 },
  { label: '3 dny před začátkem platnosti', value: 3 },
  { label: 'Týden před začátkem platnosti', value: 7 },
];

function noticeDay(version: TimetableVersion, daysBefore: number): string {
  const day = new Date(`${version.validFrom}T00:00:00`);
  day.setDate(day.getDate() - daysBefore);
  return toDateOnly(day);
}

/** Administrace → Výuka: a button with the version its sections work with;
 * its popover picks another, and for whoever edits the timetable makes a new
 * draft (empty or a copy), changes its name and start, publishes, archives
 * and brings an archived one back. */
@Component({
  selector: 'app-timetable-versions',
  imports: [
    ...FORM_FIELDS,
    Button,
    DatePicker,
    Dialog,
    FloatField,
    FormsModule,
    InputText,
    Loading,
    Message,
    Popover,
    Select,
    Tag,
  ],
  templateUrl: './timetable-versions.html',
})
export class TimetableVersions {
  private readonly selection = inject(TimetableVersionSelection);
  private readonly versionsApi = inject(TimetableVersionsApiService);
  private readonly substitutionsApi = inject(SubstitutionsApiService);
  private readonly confirmation = inject(ConfirmationService);
  private readonly notify = inject(NotifyService);

  /** False = only to pick one. */
  readonly canEdit = input(true);

  private readonly popover = viewChild.required(Popover);
  protected readonly versions = this.selection.versions;
  protected readonly current = this.selection.version;

  protected readonly today = toDateOnly(new Date());
  protected readonly statusLabel = versionStatusLabel;
  protected readonly validity = versionValidity;

  protected toggle(event: Event): void {
    this.popover().toggle(event);
  }

  protected pick(version: TimetableVersion): void {
    this.selection.versionId.set(version.id);
    this.popover().hide();
  }

  /** null = none; 'new' = a new draft; a number = editing that version. */
  protected readonly form = signal<'new' | number | null>(null);
  protected formName = '';
  protected formValidFrom: Date | null = null;
  protected formCopyFrom: number | null = null;

  protected readonly copyOptions = computed(() => [
    { label: 'Prázdný rozvrh', value: null },
    ...this.versions()
      .filter((v) => v.status !== 'Archived')
      .map((v) => ({ label: v.name, value: v.id as number | null })),
  ]);

  /** Published, but doesn't apply yet. */
  protected canUnpublish(version: TimetableVersion): boolean {
    return version.status === 'Published' && version.validFrom > this.today;
  }

  protected canArchive(version: TimetableVersion): boolean {
    return (
      version.status === 'Draft' ||
      (version.status === 'Published' && version.validFrom > this.today)
    );
  }

  /** The start of a version that already applies stays. */
  protected canMoveStart(version: TimetableVersion): boolean {
    return version.status !== 'Published' || version.validFrom > this.today;
  }

  protected editingVersion(): TimetableVersion | undefined {
    const form = this.form();
    return typeof form === 'number'
      ? this.versions().find((v) => v.id === form)
      : undefined;
  }

  protected startNew(): void {
    this.popover().hide();
    this.form.set('new');
    this.formName = '';
    this.formValidFrom = null;
    const current = this.current();
    this.formCopyFrom =
      current && current.status !== 'Archived'
        ? current.id
        : (timetableVersionOn(this.versions(), this.today)?.id ?? null);
    this.notify.clear();
  }

  protected startEdit(version: TimetableVersion): void {
    this.popover().hide();
    this.form.set(version.id);
    this.formName = version.name;
    this.formValidFrom = new Date(`${version.validFrom}T00:00:00`);
    this.notify.clear();
  }

  protected closeForm(): void {
    this.form.set(null);
  }

  protected save(): void {
    if (!this.formValidFrom) {
      return;
    }
    const request = {
      name: this.formName,
      validFrom: toDateOnly(this.formValidFrom),
    };
    const form = this.form();
    const result =
      form === 'new'
        ? this.versionsApi.createVersion({
            ...request,
            copyFromVersionId: this.formCopyFrom,
          })
        : typeof form === 'number'
          ? this.versionsApi.updateVersion(form, request)
          : null;
    result?.subscribe({
      next: (version) => {
        this.form.set(null);
        if (form === 'new') {
          this.selection.versionId.set(version.id);
        }
        this.selection.changed();
      },
      error: (err) => this.notify.actionFailed('saveVersion failed', err),
    });
  }

  /** The version about to be published, with what's wrong with it (null
   * while that loads). */
  protected readonly publishing = signal<TimetableVersion | null>(null);
  protected readonly publishProblems = signal<string[] | null>(null);
  /** Days of suplování the version would apply to. */
  protected readonly publishChangedDays = signal<string[]>([]);
  protected publishNotifyDaysBefore: number | null = null;
  /** Only the days still ahead. */
  protected readonly publishNotifyOptions = computed(() => {
    const version = this.publishing();
    return NOTIFY_OPTIONS.filter(
      (o) =>
        o.value === null ||
        (version && noticeDay(version, o.value) > this.today),
    );
  });

  protected publish(version: TimetableVersion): void {
    this.popover().hide();
    this.publishing.set(version);
    this.publishProblems.set(null);
    this.publishNotifyDaysBefore = null;
    this.publishChangedDays.set([]);
    this.substitutionsApi.getChangedDays(version.validFrom).subscribe({
      next: (days) => this.publishChangedDays.set(days),
      error: (err) => console.error('getChangedDays failed', err),
    });
    this.versionsApi.getProblems(version.id).subscribe({
      next: (problems) => this.publishProblems.set(problems),
      error: (err) => {
        console.error('getProblems failed', err);
        this.publishProblems.set([]);
      },
    });
  }

  protected publishMessage(version: TimetableVersion): string {
    const replaced = timetableVersionOn(this.versions(), version.validFrom);
    return `Zveřejnit „${version.name}“? Od ${formatDate(version.validFrom, 'date')} ji uvidí všichni${replaced ? ` a nahradí verzi „${replaced.name}“` : ''}.`;
  }

  protected changedDaysMessage(days: string[]): string {
    const n = days.length;
    const count = `${n} ${n === 1 ? 'den' : n < 5 ? 'dny' : 'dní'}`;
    return `Od ${formatDate(days[0], 'date')} je připravené nebo zveřejněné suplování (${count}). Po zveřejnění ho zkontrolujte v Suplování: změny hodin, které nová verze nemá, se nepoužijí.`;
  }

  protected confirmPublish(version: TimetableVersion): void {
    const days = this.publishNotifyDaysBefore;
    this.publishing.set(null);
    this.run(
      this.versionsApi.publishVersion(version.id, days),
      'publish',
      days === null
        ? 'Verze rozvrhu je zveřejněná, dotčení dostanou upozornění dnes večer.'
        : `Verze rozvrhu je zveřejněná, dotčení dostanou upozornění ${formatDate(noticeDay(version, days), 'date')} večer.`,
    );
  }

  protected unpublish(version: TimetableVersion): void {
    this.popover().hide();
    this.confirmation.confirm({
      header: 'Zrušit zveřejnění verze rozvrhu',
      message: `Vrátit „${version.name}“ mezi koncepty? Od ${formatDate(version.validFrom, 'date')} platit nebude a uvidí ji jen ti, kdo připravují rozvrh. Kdo už dostal upozornění na změnu, dostane večer oznámení, pokud ji do té doby znovu nezveřejníte.`,
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Zrušit zveřejnění',
      rejectLabel: 'Ponechat',
      rejectButtonProps: { severity: 'secondary', outlined: true },
      accept: () =>
        this.run(
          this.versionsApi.unpublishVersion(version.id),
          'unpublish',
          'Verze rozvrhu je zpět mezi koncepty.',
        ),
    });
  }

  protected archive(version: TimetableVersion): void {
    this.popover().hide();
    this.confirmation.confirm({
      header: 'Archivovat verzi rozvrhu',
      message: `Archivovat „${version.name}“? Nebude platit a zůstane jen k nahlédnutí, případně ji půjde vrátit mezi koncepty.${version.status === 'Published' ? ' Kdo už dostal upozornění na změnu, dostane večer oznámení o zrušení.' : ''}`,
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Archivovat',
      rejectLabel: 'Zrušit',
      acceptButtonProps: { severity: 'danger' },
      rejectButtonProps: { severity: 'secondary', outlined: true },
      accept: () =>
        this.run(
          this.versionsApi.archiveVersion(version.id),
          'archive',
          'Verze rozvrhu je archivovaná.',
        ),
    });
  }

  protected restore(version: TimetableVersion): void {
    this.run(
      this.versionsApi.restoreVersion(version.id),
      'restore',
      'Verze rozvrhu je zpět mezi koncepty.',
    );
  }

  private run(
    action: Observable<TimetableVersion>,
    name: string,
    done: string,
  ): void {
    action.subscribe({
      next: () => {
        this.notify.success(done);
        this.selection.changed();
      },
      error: (err) => this.notify.actionFailed(`${name}Version failed`, err),
    });
  }
}

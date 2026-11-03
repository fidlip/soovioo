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
import { ConfirmationService } from '@openng/optimus-ui/api';
import { DatePicker } from '@openng/optimus-ui/datepicker';
import { Dialog } from '@openng/optimus-ui/dialog';
import { InputText } from '@openng/optimus-ui/inputtext';
import { Select } from '@openng/optimus-ui/select';
import { Tag } from '@openng/optimus-ui/tag';
import {
  TimetableVersion,
  TimetableVersionsApiService,
  formatDate,
  timetableVersionOn,
} from '@soovioo/shared';
import { Observable } from 'rxjs';
import { toDateOnly } from '../departures/retention-labels';
import { FORM_FIELDS, FloatField } from '../forms';
import { NotifyService } from '../notify.service';

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

/** Versions of the timetable: a new draft (empty or a copy), its name and
 * start, publishing, archiving and bringing an archived one back. */
@Component({
  selector: 'app-timetable-versions-dialog',
  imports: [
    FloatField,
    ...FORM_FIELDS,
    FormsModule,
    Button,
    DatePicker,
    Dialog,
    InputText,
    Select,
    Tag,
  ],
  templateUrl: './timetable-versions-dialog.html',
})
export class TimetableVersionsDialog {
  private readonly versionsApi = inject(TimetableVersionsApiService);
  private readonly confirmation = inject(ConfirmationService);
  private readonly notify = inject(NotifyService);

  readonly visible = input.required<boolean>();
  /** False = only to look at the versions. */
  readonly canEdit = input(true);
  readonly versions = input.required<TimetableVersion[]>();
  /** The version the timetable shows now — the default to copy from. */
  readonly shownVersionId = input<number | null>(null);
  readonly closed = output<void>();
  readonly changed = output<void>();
  readonly show = output<number>();

  protected readonly today = toDateOnly(new Date());
  protected readonly statusLabel = versionStatusLabel;
  protected readonly validity = versionValidity;

  /** null = the list; 'new' = a new draft; a number = editing that version. */
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
    this.form.set('new');
    this.formName = '';
    this.formValidFrom = null;
    this.formCopyFrom =
      this.shownVersionId() ??
      timetableVersionOn(this.versions(), this.today)?.id ??
      null;
    this.notify.clear();
  }

  protected startEdit(version: TimetableVersion): void {
    this.form.set(version.id);
    this.formName = version.name;
    this.formValidFrom = new Date(`${version.validFrom}T00:00:00`);
    this.notify.clear();
  }

  protected close(): void {
    this.form.set(null);
    this.closed.emit();
  }

  protected cancelForm(): void {
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
        this.changed.emit();
        if (form === 'new') {
          this.show.emit(version.id);
        }
      },
      error: (err) => this.notify.actionFailed('saveVersion failed', err),
    });
  }

  protected publish(version: TimetableVersion): void {
    const replaced = timetableVersionOn(this.versions(), version.validFrom);
    this.confirmation.confirm({
      header: 'Zveřejnit verzi rozvrhu',
      message: `Zveřejnit „${version.name}“? Od ${formatDate(version.validFrom, 'date')} ji uvidí všichni${replaced ? ` a nahradí verzi „${replaced.name}“` : ''}.`,
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Zveřejnit',
      rejectLabel: 'Zrušit',
      rejectButtonProps: { severity: 'secondary', outlined: true },
      accept: () =>
        this.run(
          this.versionsApi.publishVersion(version.id),
          'publish',
          'Verze rozvrhu je zveřejněná.',
        ),
    });
  }

  protected unpublish(version: TimetableVersion): void {
    this.confirmation.confirm({
      header: 'Zrušit zveřejnění verze rozvrhu',
      message: `Vrátit „${version.name}“ mezi koncepty? Od ${formatDate(version.validFrom, 'date')} platit nebude a uvidí ji jen ti, kdo připravují rozvrh.`,
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
    this.confirmation.confirm({
      header: 'Archivovat verzi rozvrhu',
      message: `Archivovat „${version.name}“? Nebude platit a zůstane jen k nahlédnutí, případně ji půjde vrátit mezi koncepty.`,
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
        this.changed.emit();
      },
      error: (err) => this.notify.actionFailed(`${name}Version failed`, err),
    });
  }
}

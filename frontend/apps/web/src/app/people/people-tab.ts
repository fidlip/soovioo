import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ConfirmationService, PrimeTemplate } from '@openng/optimus-ui/api';
import { Button } from '@openng/optimus-ui/button';
import { Card } from '@openng/optimus-ui/card';
import { InputText } from '@openng/optimus-ui/inputtext';
import { Select } from '@openng/optimus-ui/select';
import { TableModule } from '@openng/optimus-ui/table';
import { Tag } from '@openng/optimus-ui/tag';
import {
  AuthApiService,
  DateFormatPipe,
  PeopleApiService,
  Person,
  PhotosApiService,
  Student,
  localizeStudentTerm,
} from '@soovioo/shared';
import { Avatar } from '../avatar/avatar';
import { LeaveDialog, LeaveTarget } from '../departures/leave-dialog';
import { NotifyService } from '../notify.service';
import {
  StudentPhotoDialog,
  UserPhotoDialog,
} from '../photo-dialogs/photo-dialogs';
import { TermPipe } from '../term.pipe';
import { foldForSearch } from '../text-search';
import { PersonDialog, PersonDialogTarget } from './person-dialog';
import { personPhotoUrl } from './person-photo';

type Kind = 'teacher' | 'parent' | 'student' | 'noAccount';

/** Administrace → Osoby: everyone at the school with their profiles, with
 * or without an account. */
@Component({
  selector: 'app-people-tab',
  imports: [
    Avatar,
    Button,
    Card,
    DateFormatPipe,
    FormsModule,
    InputText,
    LeaveDialog,
    PersonDialog,
    PrimeTemplate,
    Select,
    StudentPhotoDialog,
    TableModule,
    Tag,
    TermPipe,
    UserPhotoDialog,
  ],
  templateUrl: './people-tab.html',
})
export class PeopleTab {
  private readonly peopleApi = inject(PeopleApiService);
  private readonly photosApi = inject(PhotosApiService);
  private readonly authApi = inject(AuthApiService);
  private readonly confirmation = inject(ConfirmationService);
  private readonly notify = inject(NotifyService);

  /** "Vytvořit účet" — the account dialog lives in Uživatelé. */
  readonly createAccount = output<Person>();
  /** Something an account shows (name, contacts) changed. */
  readonly changed = output<void>();

  protected readonly people = signal<Person[]>([]);
  protected readonly dialog = signal<PersonDialogTarget | null>(null);
  protected readonly leaveStudent = signal<LeaveTarget | null>(null);
  protected readonly photoStudentId = signal<number | null>(null);
  protected readonly photoUser = signal<{
    id: number;
    name: string;
    photo?: string | null;
  } | null>(null);

  protected readonly filterName = signal('');
  protected readonly filterKind = signal<Kind | null>(null);
  protected readonly filterClassId = signal<number | null>(null);

  protected readonly kindOptions: { label: string; value: Kind }[] = [
    { label: 'Učitelé', value: 'teacher' },
    { label: 'Rodiče', value: 'parent' },
    {
      label: localizeStudentTerm('Žáci', this.authApi.getSchoolType()),
      value: 'student',
    },
    { label: 'Bez účtu', value: 'noAccount' },
  ];

  protected readonly classOptions = computed(() => {
    const classes = new Map<number, string>();
    for (const p of this.people()) {
      if (p.student) {
        classes.set(p.student.schoolClassId, p.student.schoolClassName);
      }
    }
    return [...classes]
      .map(([id, name]) => ({ id, name }))
      .sort((a, b) => a.name.localeCompare(b.name, 'cs'));
  });

  protected readonly filtered = computed(() => {
    const name = foldForSearch(this.filterName().trim());
    const kind = this.filterKind();
    const classId = this.filterClassId();
    return this.people().filter(
      (p) =>
        (!name || foldForSearch(p.name).includes(name)) &&
        (kind === null ||
          (kind === 'teacher' && !!p.teacher) ||
          (kind === 'parent' && !!p.parent) ||
          (kind === 'student' && !!p.student) ||
          (kind === 'noAccount' && !p.account)) &&
        (classId === null || p.student?.schoolClassId === classId),
    );
  });

  constructor() {
    this.reload();
  }

  reload(): void {
    this.peopleApi.getPeople().subscribe({
      next: (people) => this.people.set(people),
      error: (err) => this.notify.actionFailed('getPeople failed', err),
    });
  }

  protected childNames(person: Person): string {
    return (person.parent?.children ?? []).map((c) => c.name).join(', ');
  }

  protected photoUrl(person: Person): string | null {
    return personPhotoUrl(this.photosApi, person);
  }

  /** Pupils' photos go with consent; staff photos are the admin's to set;
   * parents set their own. */
  protected canSetPhoto(person: Person): boolean {
    return (
      !!person.student ||
      (!!person.account && person.account.roles.some((r) => r.kind === 'Staff'))
    );
  }

  protected openPhoto(person: Person): void {
    if (person.student) {
      this.photoStudentId.set(person.student.id);
    } else if (person.account) {
      this.photoUser.set({
        id: person.account.userId,
        name: person.name,
        photo: person.photo,
      });
    }
  }

  protected onSaved(): void {
    this.dialog.set(null);
    this.reload();
    this.changed.emit();
  }

  protected openLeave(student: Student): void {
    this.leaveStudent.set({
      ...student,
      reason: student.leaveReason,
      reasonDetail: student.leaveReasonDetail,
      note: student.leaveNote,
    });
  }

  /** Only someone without an account; a pupil only without history — the
   * server answers 409 with what's there, and Ukončení vzdělávání is
   * offered instead. */
  protected delete(person: Person): void {
    this.confirmation.confirm({
      header: 'Smazat osobu',
      message: person.student
        ? localizeStudentTerm(
            `Opravdu smazat žáka ${person.name}? Smazat jde jen žáka bez záznamů (založeného omylem), jinak se ukončuje jeho vzdělávání.`,
            this.authApi.getSchoolType(),
          )
        : `Opravdu smazat osobu ${person.name}?`,
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Smazat',
      rejectLabel: 'Zrušit',
      acceptButtonProps: { severity: 'danger' },
      rejectButtonProps: { severity: 'secondary', outlined: true },
      accept: () =>
        this.peopleApi.deletePerson(person.id).subscribe({
          next: () => this.reload(),
          error: (err) => {
            if (err instanceof HttpErrorResponse && err.status === 409) {
              this.refused(person, String(err.error));
            } else {
              this.notify.actionFailed('deletePerson failed', err);
            }
          },
        }),
    });
  }

  private refused(person: Person, reason: string): void {
    const student = person.student;
    if (!student || person.account) {
      this.notify.warn(reason);
      return;
    }
    // After the delete confirmation has gone.
    setTimeout(() =>
      this.confirmation.confirm({
        header: localizeStudentTerm(
          'Žáka nejde smazat',
          this.authApi.getSchoolType(),
        ),
        message: reason,
        icon: 'pi pi-info-circle',
        acceptLabel: 'Ukončit vzdělávání',
        rejectLabel: 'Zavřít',
        rejectButtonProps: { severity: 'secondary', outlined: true },
        accept: () => this.openLeave(student),
      }),
    );
  }
}

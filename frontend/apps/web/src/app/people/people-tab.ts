import { HttpErrorResponse } from '@angular/common/http';
import {
  Component,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
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
  RoleRef,
  Student,
  localizeStudentTerm,
} from '@soovioo/shared';
import { Avatar } from '../avatar/avatar';
import { ClearFilters, FloatField } from '../forms';
import { LeaveDialog, LeaveTarget } from '../departures/leave-dialog';
import { NotifyService } from '../notify.service';
import {
  StudentPhotoDialog,
  UserPhotoDialog,
} from '../photo-dialogs/photo-dialogs';
import { TermPipe } from '../term.pipe';
import { foldForSearch, matchesContact } from '../text-search';
import { PersonDialog, PersonDialogTarget } from './person-dialog';
import { personPhotoUrl } from './person-photo';
import { Loading } from '../loading/loading';
import { NicknameApproval } from '../nickname-approval/nickname-approval';

type Kind = 'teacher' | 'parent' | 'student' | 'noAccount';

/** A pupil of the class, a parent of one, its class teacher or a teacher of it. */
function isInClass(person: Person, classId: number): boolean {
  return (
    person.student?.schoolClassId === classId ||
    !!person.parent?.children.some((c) => c.schoolClassId === classId) ||
    !!person.teacher?.classTeacherOfIds.includes(classId) ||
    !!person.teacher?.teachesClassIds.includes(classId)
  );
}

/** Administrace → Osoby: everyone at the school with their profiles, with
 * or without an account. */
@Component({
  selector: 'app-people-tab',
  imports: [
    NicknameApproval,
    Avatar,
    Button,
    Card,
    DateFormatPipe,
    ClearFilters,
    FloatField,
    FormsModule,
    InputText,
    LeaveDialog,
    Loading,
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

  /** The school's roles (Administrace → Role). */
  readonly roles = input<RoleRef[]>([]);
  readonly classes = input<{ id: number; name: string }[]>([]);
  readonly subjects = input<{ id: number; name: string }[]>([]);

  /** "Vytvořit účet" — the account dialog lives in Uživatelé. */
  readonly createAccount = output<Person>();
  /** Something an account shows (name, contacts) changed. */
  readonly changed = output<void>();
  /** Přidělení výuky of the teacher (their user id). */
  readonly showTeaching = output<number>();

  protected readonly people = signal<Person[] | null>(null);
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
  protected readonly filterRoleId = signal<number | null>(null);
  protected readonly filterClassId = signal<number | null>(null);
  protected readonly filterContact = signal('');
  protected readonly filterSubjectId = signal<number | null>(null);
  protected readonly filterClassTeacherOf = signal<number | null>(null);
  protected readonly filterTeaches = signal<number | null>(null);
  protected readonly filterChild = signal('');

  protected readonly kindOptions: { label: string; value: Kind }[] = [
    { label: 'Učitelé', value: 'teacher' },
    { label: 'Rodiče', value: 'parent' },
    {
      label: localizeStudentTerm('Žáci', this.authApi.getSchoolType()),
      value: 'student',
    },
    { label: 'Bez účtu', value: 'noAccount' },
  ];

  protected readonly classOptions = computed(() =>
    [...this.classes()].sort((a, b) => a.name.localeCompare(b.name, 'cs')),
  );

  /** The subjects someone at the school is qualified for. */
  protected readonly qualificationOptions = computed(() => {
    const qualified = new Set(
      (this.people() ?? []).flatMap(
        (p) => p.teacher?.qualifications.map((q) => q.subjectId) ?? [],
      ),
    );
    return this.subjects().filter((s) => qualified.has(s.id));
  });

  protected readonly filtered = computed(() => {
    const name = foldForSearch(this.filterName().trim());
    const kind = this.filterKind();
    const roleId = this.filterRoleId();
    const classId = this.filterClassId();
    const contact = this.filterContact().trim();
    const subjectId = this.filterSubjectId();
    const classTeacherOf = this.filterClassTeacherOf();
    const teaches = this.filterTeaches();
    const child = foldForSearch(this.filterChild().trim());
    return (this.people() ?? []).filter(
      (p) =>
        (!name || foldForSearch(p.name).includes(name)) &&
        (kind === null ||
          (kind === 'teacher' && !!p.teacher) ||
          (kind === 'parent' && !!p.parent) ||
          (kind === 'student' && !!p.student) ||
          (kind === 'noAccount' && !p.account)) &&
        (roleId === null || !!p.account?.roles.some((r) => r.id === roleId)) &&
        (classId === null || isInClass(p, classId)) &&
        (!contact || matchesContact(p, contact)) &&
        (subjectId === null ||
          !!p.teacher?.qualifications.some((q) => q.subjectId === subjectId)) &&
        (classTeacherOf === null ||
          !!p.teacher?.classTeacherOfIds.includes(classTeacherOf)) &&
        (teaches === null || !!p.teacher?.teachesClassIds.includes(teaches)) &&
        (!child ||
          !!p.parent?.children.some((c) =>
            foldForSearch(c.name).includes(child),
          )),
    );
  });

  protected readonly hasFilters = computed(
    () =>
      !!this.filterName().trim() ||
      !!this.filterContact().trim() ||
      this.filterKind() !== null ||
      this.filterRoleId() !== null ||
      this.filterClassId() !== null,
  );

  constructor() {
    this.reload();
  }

  /** The filters of another kind go with their fields. */
  protected setKind(kind: Kind | null): void {
    this.filterKind.set(kind);
    this.filterSubjectId.set(null);
    this.filterClassTeacherOf.set(null);
    this.filterTeaches.set(null);
    this.filterChild.set('');
  }

  protected clearFilters(): void {
    this.filterName.set('');
    this.filterContact.set('');
    this.filterRoleId.set(null);
    this.filterClassId.set(null);
    this.setKind(null);
  }

  reload(): void {
    this.peopleApi.getPeople().subscribe({
      next: (people) => this.people.set(people),
      error: (err) => {
        this.notify.actionFailed('getPeople failed', err);
        this.people.update((people) => people ?? []);
      },
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

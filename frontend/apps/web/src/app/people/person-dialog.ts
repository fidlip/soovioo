import {
  Component,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Dialog } from '@openng/optimus-ui/dialog';
import { InputText } from '@openng/optimus-ui/inputtext';
import { MultiSelect } from '@openng/optimus-ui/multiselect';
import { Button } from '@openng/optimus-ui/button';
import { InputNumber } from '@openng/optimus-ui/inputnumber';
import { Select } from '@openng/optimus-ui/select';
import { SelectButton } from '@openng/optimus-ui/selectbutton';
import { HttpErrorResponse } from '@angular/common/http';
import { ConfirmationService } from '@openng/optimus-ui/api';
import {
  AuthApiService,
  Country,
  DayOfWeek,
  PeopleApiService,
  PhotosApiService,
  Person,
  ReferenceApiService,
  SavePersonRequest,
  SchoolClass,
  SchoolClassesApiService,
  Student,
  StudentsApiService,
  Subject,
  SubjectsApiService,
  localizeStudentTerm,
} from '@soovioo/shared';
import { Avatar } from '../avatar/avatar';
import { FORM_FIELDS, FloatField } from '../forms';
import { NotifyService } from '../notify.service';
import { TermPipe } from '../term.pipe';
import {
  StudentFields,
  StudentForm,
  emptyStudentForm,
  matrikaOf,
  studentFormOf,
} from './student-fields';
import { personPhotoUrl } from './person-photo';
import { Loading } from '../loading/loading';

export type PersonProfile = 'teacher' | 'parent' | 'student';

interface QualificationForm {
  subjectId: number | null;
  gradeFrom: number | null;
  gradeTo: number | null;
}

/** null id = a new person, with `profiles` ticked to start with. */
export interface PersonDialogTarget {
  id: number | null;
  profiles?: PersonProfile[];
}

/** Administrace → Osoby and the account dialog: a person's name, contacts
 * and profiles (učitel, rodič, žák) in one form. Open while `target` is set. */
@Component({
  selector: 'app-person-dialog',
  imports: [
    FloatField,
    Avatar,
    Button,
    Dialog,
    InputNumber,
    Select,
    SelectButton,
    FormsModule,
    InputText,
    Loading,
    MultiSelect,
    StudentFields,
    TermPipe,
    ...FORM_FIELDS,
  ],
  templateUrl: './person-dialog.html',
})
export class PersonDialog {
  private readonly peopleApi = inject(PeopleApiService);
  private readonly studentsApi = inject(StudentsApiService);
  private readonly schoolClassesApi = inject(SchoolClassesApiService);
  private readonly subjectsApi = inject(SubjectsApiService);
  private readonly referenceApi = inject(ReferenceApiService);
  private readonly notify = inject(NotifyService);
  private readonly photosApi = inject(PhotosApiService);
  private readonly confirmation = inject(ConfirmationService);
  private readonly authApi = inject(AuthApiService);

  readonly target = input<PersonDialogTarget | null>(null);
  /** Offer Ukončení vzdělávání when a pupil's profile with history can't
   * be removed (`endEducation` then opens it). */
  readonly canEndEducation = input(false);
  readonly closed = output<void>();
  readonly saved = output<Person>();
  readonly endEducation = output<Student>();

  protected readonly visible = computed(() => this.target() !== null);
  protected readonly loading = signal(false);
  protected readonly busy = signal(false);
  /** The person as loaded — null for a new one. */
  protected readonly person = signal<Person | null>(null);
  protected readonly photoUrl = computed(() => {
    const person = this.person();
    return person ? personPhotoUrl(this.photosApi, person, 'profile') : null;
  });
  protected readonly classes = signal<SchoolClass[]>([]);
  protected readonly countries = signal<Country[]>([]);
  protected readonly subjects = signal<Subject[]>([]);
  protected readonly dayOptions: { label: string; value: DayOfWeek }[] = [
    { label: 'Po', value: 'Monday' },
    { label: 'Út', value: 'Tuesday' },
    { label: 'St', value: 'Wednesday' },
    { label: 'Čt', value: 'Thursday' },
    { label: 'Pá', value: 'Friday' },
  ];
  private readonly students = signal<Student[]>([]);
  protected readonly childOptions = computed(() =>
    this.students()
      .filter((s) => s.id !== this.person()?.student?.id)
      .map((s) => ({
        id: s.id,
        label: `${s.firstName} ${s.lastName} (${s.schoolClassName})`,
      })),
  );

  protected firstName = '';
  protected lastName = '';
  protected phone = '';
  protected email = '';
  protected profiles: PersonProfile[] = [];
  protected qualifications: QualificationForm[] = [];
  protected qualificationNote = '';
  protected contractedHours: number | null = null;
  protected teachingDays: DayOfWeek[] = [];
  protected childIds: number[] = [];
  protected studentForm: StudentForm = emptyStudentForm(null);

  /** A profile the account's role needs can't be removed here — the
   * server refuses it too. */
  private readonly locked = computed(() => {
    const p = this.person();
    const roles = p?.account?.roles ?? [];
    return {
      teacher: !!p?.teacher && roles.some((r) => r.builtIn === 'Teacher'),
      parent: !!p?.parent && roles.some((r) => r.kind === 'Parent'),
      student: !!p?.student && roles.some((r) => r.kind === 'Student'),
    };
  });

  protected readonly profileOptions = computed(() => {
    const locked = this.locked();
    return [
      { value: 'teacher', label: 'Učitel', disabled: locked.teacher },
      { value: 'parent', label: 'Rodič', disabled: locked.parent },
      {
        value: 'student',
        label: localizeStudentTerm('Žák', this.authApi.getSchoolType()),
        disabled: locked.student,
      },
    ];
  });

  /** A locked profile's chip comes back if removed. */
  protected setProfiles(profiles: PersonProfile[]): void {
    const locked = this.profileOptions()
      .filter((o) => o.disabled && !profiles.includes(o.value as PersonProfile))
      .map((o) => o.value as PersonProfile);
    this.profiles = [...profiles, ...locked];
  }

  protected readonly lockReasons = computed(() =>
    this.profileOptions()
      .filter((o) => o.disabled)
      .map(
        (o) =>
          `${o.label}: účet má tuto roli — profil jde odebrat až s ní (Uživatelé).`,
      ),
  );

  constructor() {
    effect(() => {
      const target = this.target();
      if (target) {
        untracked(() => this.open(target));
      }
    });
  }

  private open(target: PersonDialogTarget): void {
    this.notify.clear();
    this.person.set(null);
    this.firstName = '';
    this.lastName = '';
    this.phone = '';
    this.email = '';
    this.profiles = [...(target.profiles ?? [])];
    this.qualifications = [];
    this.qualificationNote = '';
    this.contractedHours = null;
    this.teachingDays = this.dayOptions.map((d) => d.value);
    this.childIds = [];
    this.studentForm = emptyStudentForm(null);
    this.loadLists();
    if (target.id === null) {
      return;
    }
    this.loading.set(true);
    this.peopleApi.getPerson(target.id).subscribe({
      next: (person) => {
        this.person.set(person);
        this.firstName = person.firstName;
        this.lastName = person.lastName;
        this.phone = person.phone ?? '';
        this.email = person.email ?? '';
        this.profiles = [
          ...(person.teacher ? ['teacher' as const] : []),
          ...(person.parent ? ['parent' as const] : []),
          ...(person.student ? ['student' as const] : []),
        ];
        if (person.teacher) {
          this.qualifications = person.teacher.qualifications.map((q) => ({
            ...q,
          }));
          this.qualificationNote = person.teacher.qualificationNote ?? '';
          this.contractedHours = person.teacher.contractedHours;
          this.teachingDays = [...person.teacher.teachingDays];
        }
        this.childIds = person.parent?.children.map((c) => c.studentId) ?? [];
        if (person.student) {
          this.studentForm = studentFormOf(person.student);
        }
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.notify.actionFailed('getPerson failed', err);
      },
    });
  }

  private loadLists(): void {
    this.schoolClassesApi.getSchoolClasses().subscribe({
      next: (classes) => {
        this.classes.set(classes);
        this.studentForm.schoolClassId ??= classes[0]?.id ?? null;
      },
      error: (err) => console.error('getSchoolClasses failed', err),
    });
    this.subjectsApi.getSubjects().subscribe({
      next: (subjects) => this.subjects.set(subjects),
      error: (err) => console.error('getSubjects failed', err),
    });
    this.studentsApi.getStudents().subscribe({
      next: (students) => this.students.set(students),
      error: (err) => console.error('getStudents failed', err),
    });
    if (this.countries().length === 0) {
      this.referenceApi.getCountries().subscribe({
        next: (countries) => this.countries.set(countries),
        error: (err) => console.error('getCountries failed', err),
      });
    }
  }

  protected save(): void {
    const request: SavePersonRequest = {
      firstName: this.firstName.trim(),
      lastName: this.lastName.trim(),
      phone: this.phone.trim() || undefined,
      email: this.email.trim() || undefined,
      teacher: this.profiles.includes('teacher')
        ? {
            qualifications: this.qualifications
              .filter((q) => q.subjectId !== null)
              .map((q) => ({
                subjectId: q.subjectId!,
                gradeFrom: q.gradeFrom,
                gradeTo: q.gradeTo,
              })),
            qualificationNote: this.qualificationNote.trim() || undefined,
            contractedHours: this.contractedHours,
            teachingDays: this.teachingDays,
          }
        : undefined,
      parent: this.profiles.includes('parent')
        ? { childIds: this.childIds }
        : undefined,
      student: this.profiles.includes('student')
        ? matrikaOf(this.studentForm)
        : undefined,
    };
    const id = this.person()?.id ?? null;
    this.busy.set(true);
    (id === null
      ? this.peopleApi.addPerson(request)
      : this.peopleApi.updatePerson(id, request)
    ).subscribe({
      next: (person) => {
        this.busy.set(false);
        this.saved.emit(person);
      },
      error: (err) => {
        this.busy.set(false);
        const student = this.person()?.student;
        if (err instanceof HttpErrorResponse && err.status === 409 && student) {
          this.refused(student, String(err.error));
        } else {
          this.notify.actionFailed('savePerson failed', err);
        }
      },
    });
  }

  protected addQualification(): void {
    this.qualifications = [
      ...this.qualifications,
      { subjectId: null, gradeFrom: null, gradeTo: null },
    ];
  }

  protected removeQualification(index: number): void {
    this.qualifications = this.qualifications.filter((_, i) => i !== index);
  }

  /** The pupil's profile has history — their education is ended instead. */
  private refused(student: Student, reason: string): void {
    if (!this.canEndEducation()) {
      this.notify.warn(reason);
      return;
    }
    this.confirmation.confirm({
      header: localizeStudentTerm(
        'Profil žáka nejde odebrat',
        this.authApi.getSchoolType(),
      ),
      message: reason,
      icon: 'pi pi-info-circle',
      acceptLabel: 'Ukončit vzdělávání',
      rejectLabel: 'Zavřít',
      rejectButtonProps: { severity: 'secondary', outlined: true },
      accept: () => this.endEducation.emit(student),
    });
  }
}

import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Button } from '@openng/optimus-ui/button';
import { Checkbox } from '@openng/optimus-ui/checkbox';
import { Card } from '@openng/optimus-ui/card';
import { DatePicker } from '@openng/optimus-ui/datepicker';
import { Dialog } from '@openng/optimus-ui/dialog';
import { Message as PMessage } from '@openng/optimus-ui/message';
import { MultiSelect } from '@openng/optimus-ui/multiselect';
import { Select } from '@openng/optimus-ui/select';
import { Tag } from '@openng/optimus-ui/tag';
import { Textarea } from '@openng/optimus-ui/textarea';
import {
  AuthApiService,
  CreateHomeworkRequest,
  DateFormatPipe,
  Homework,
  HomeworkApiService,
  HomeworkAttachment,
  SchoolClass,
  SchoolClassesApiService,
  Student,
  StudentReach,
  StudentsApiService,
  Subject,
  SubjectsApiService,
  TeacherTimetableEntry,
  TimetableApiService,
} from '@soovioo/shared';
import { AppIcon } from '../app-icon';
import { FilePicker } from '../file-picker/file-picker';
import { NotifyService } from '../notify.service';
import { FORM_FIELDS, FloatField } from '../forms';
import { Loading } from '../loading/loading';

/** Mirrors MessageAttachmentRules on the backend — same allow-list, reused
 * for homework attachments too (see ROADMAP "Přílohy u zpráv"). */
const MAX_ATTACHMENT_SIZE_BYTES = 10 * 1024 * 1024;
const ALLOWED_ATTACHMENT_EXTENSIONS = [
  '.pdf',
  '.jpg',
  '.jpeg',
  '.png',
  '.gif',
  '.webp',
  '.txt',
  '.doc',
  '.docx',
  '.xls',
  '.xlsx',
  '.ppt',
  '.pptx',
  '.odt',
  '.ods',
  '.odp',
];

function assignmentKey(
  schoolClassId: number | null,
  studentGroupId: number | null,
  subjectId: number,
): string {
  return studentGroupId !== null
    ? `g${studentGroupId}`
    : `${schoolClassId}-${subjectId}`;
}

@Component({
  selector: 'app-homework-page',
  imports: [
    FloatField,
    DateFormatPipe,
    ...FORM_FIELDS,
    FilePicker,
    Checkbox,
    AppIcon,
    Button,
    Card,
    DatePicker,
    Dialog,
    FormsModule,
    Loading,
    MultiSelect,
    PMessage,
    Select,
    Tag,
    Textarea,
  ],
  templateUrl: './homework-page.html',
})
export class HomeworkPage {
  protected readonly notify = inject(NotifyService);
  private readonly homeworkApi = inject(HomeworkApiService);
  private readonly studentsApi = inject(StudentsApiService);
  private readonly subjectsApi = inject(SubjectsApiService);
  private readonly schoolClassesApi = inject(SchoolClassesApiService);
  private readonly timetableApi = inject(TimetableApiService);
  protected readonly authApi = inject(AuthApiService);
  /** Any class and subject — otherwise what the timetable says they teach. */
  protected readonly anyClass =
    this.authApi.scope('HomeworkWrite') === 'School';

  protected readonly homeworkItems = signal<Homework[] | null>(null);
  protected readonly students = signal<Student[]>([]);
  protected readonly subjects = signal<Subject[]>([]);
  /** Not a row of the plan its divisions teach. */
  protected readonly givenSubjects = computed(() =>
    this.subjects().filter((s) => !s.coveredByDivisions),
  );
  protected readonly schoolClasses = signal<SchoolClass[]>([]);
  /** Only populated (and only relevant) for a Teacher — the exact
   * class+subject combinations they actually teach, reused from the
   * Rozvrh endpoint instead of adding a new one just for this picker. */
  protected readonly myTeachingAssignments = signal<TeacherTimetableEntry[]>(
    [],
  );

  protected readonly studentsById = computed(
    () => new Map(this.students().map((s) => [s.id, s])),
  );

  protected filterClassId: number | null = null;
  protected filterSubjectId: number | null = null;

  /** Staff pick from every class, a parent or pupil only from their
   * children's (the students list holds just those). */
  protected readonly classOptions = computed(() => {
    const own = new Set(this.students().map((s) => s.schoolClassId));
    return this.schoolClasses()
      .filter((c) => this.canManageHomework() || own.has(c.id))
      .map((c) => ({ id: c.id, label: c.name }));
  });
  /** Whom HomeworkWrite reaches — for a Teacher the pupils of their
   * groups, offered for a group's homework. */
  private readonly reach = signal<StudentReach | null>(null);

  /** Deduplicated (class, subject) pairs and groups a Teacher actually
   * teaches — the create dialog's single "Třída a předmět" picker for that
   * role. */
  protected readonly teachingAssignmentOptions = computed(() => {
    const seen = new Set<string>();
    const options: {
      key: string;
      label: string;
      schoolClassId: number | null;
      studentGroupId: number | null;
      subjectId: number;
    }[] = [];
    for (const entry of this.myTeachingAssignments()) {
      const groupId = entry.studentGroupId ?? null;
      const key = assignmentKey(entry.schoolClassId, groupId, entry.subjectId);
      if (seen.has(key)) {
        continue;
      }
      seen.add(key);
      options.push({
        key,
        // A group's lesson is named "Nj (7.A, 7.B)" already.
        label: `${entry.schoolClassName} — ${entry.subjectName}`,
        schoolClassId: groupId === null ? entry.schoolClassId : null,
        studentGroupId: groupId,
        subjectId: entry.subjectId,
      });
    }
    return options;
  });

  protected readonly homeworkDialogVisible = signal(false);
  /** null = the dialog creates a new item, otherwise edits this one. */
  protected readonly editingHomeworkId = signal<number | null>(null);
  protected readonly periodOptions = [
    { value: 'current', label: 'Aktuální' },
    { value: 'archive', label: 'Archiv' },
  ];
  /** Aktuální = due today or later (nearest first), Archiv = past (newest
   * first). */
  protected filterPeriod: 'current' | 'archive' = 'current';
  protected readonly createError = signal<string | null>(null);
  protected readonly fileError = signal<string | null>(null);

  protected selectedAssignmentKey: string | null = null;
  protected readonly newSchoolClassId = signal<number | null>(null);
  /** Homework of a group instead of a class. */
  protected readonly newStudentGroupId = signal<number | null>(null);
  /** The edited group homework's label ("Nj (7.A, 7.B)"), and its pupils
   * known from it. */
  protected editingGroupLabel: string | null = null;
  private readonly editingGroupStudentIds = signal<number[]>([]);
  protected newSubjectId: number | null = null;
  protected newText = '';
  protected newDueDate = new Date();
  protected newRequiresCompletion = false;
  /** Empty = whole class. */
  protected newTargetStudentIds: number[] = [];
  protected selectedFile: File | null = null;

  protected readonly targetLabel = computed(() =>
    this.newStudentGroupId() !== null
      ? 'Komu (prázdné = celá skupina)'
      : 'Komu (prázdné = celá třída)',
  );
  protected readonly newClassStudentOptions = computed(() => {
    const groupId = this.newStudentGroupId();
    const inGroup = new Set([
      ...this.editingGroupStudentIds(),
      ...(this.reach()?.students ?? [])
        .filter((r) => groupId !== null && r.studentGroupIds.includes(groupId))
        .map((r) => r.studentId),
    ]);
    return this.students()
      .filter((s) =>
        groupId !== null
          ? inGroup.has(s.id)
          : s.schoolClassId === this.newSchoolClassId(),
      )
      .map((s) => ({ id: s.id, label: `${s.firstName} ${s.lastName}` }));
  });

  constructor() {
    this.reload();
    this.studentsApi.getStudents().subscribe({
      next: (students) => this.students.set(students),
      error: (err) => console.error('getStudents failed', err),
    });
    this.subjectsApi.getSubjects().subscribe({
      next: (subjects) => {
        this.subjects.set(subjects);
        this.preselectInOpenDialog();
      },
      error: (err) => console.error('getSubjects failed', err),
    });
    this.schoolClassesApi.getSchoolClasses().subscribe({
      next: (schoolClasses) => {
        this.schoolClasses.set(schoolClasses);
        this.preselectInOpenDialog();
      },
      error: (err) => console.error('getSchoolClasses failed', err),
    });
    if (!this.anyClass) {
      if (this.canManageHomework()) {
        this.studentsApi.getReach('HomeworkWrite').subscribe({
          next: (reach) => this.reach.set(reach),
          error: (err) => console.error('getReach failed', err),
        });
      }
      const teacherId = this.authApi.getUserId();
      if (teacherId) {
        this.timetableApi.getTeacherTimetable(teacherId).subscribe({
          next: (entries) => {
            this.myTeachingAssignments.set(entries);
            this.preselectInOpenDialog();
          },
          error: (err) => console.error('getTeacherTimetable failed', err),
        });
      }
    }
  }

  protected canManageHomework(): boolean {
    return this.authApi.can('HomeworkWrite');
  }

  protected filteredHomework(): Homework[] {
    const today = this.toDateOnlyString(new Date());
    const archive = this.filterPeriod === 'archive';
    return (this.homeworkItems() ?? [])
      .filter(
        (h) =>
          (this.filterClassId === null ||
            h.schoolClassIds.includes(this.filterClassId)) &&
          (this.filterSubjectId === null ||
            h.subjectId === this.filterSubjectId) &&
          (archive ? h.dueDate < today : h.dueDate >= today),
      )
      .sort((a, b) =>
        archive
          ? b.dueDate.localeCompare(a.dueDate)
          : a.dueDate.localeCompare(b.dueDate),
      );
  }

  /** The caller-visible students this homework item actually applies to —
   * Parent/Student only ever see their own linked student(s) here (the
   * backend already scopes `students()` the same way), Teacher/Admin see
   * everyone targeted. */
  protected targetedStudents(item: Homework): Student[] {
    if (item.targetStudentIds.length > 0) {
      const byId = this.studentsById();
      return item.targetStudentIds
        .map((id) => byId.get(id))
        .filter((s): s is Student => !!s);
    }
    if (item.groupStudentIds) {
      const members = new Set(item.groupStudentIds);
      return this.students().filter((s) => members.has(s.id));
    }
    return this.students().filter(
      (s) => s.schoolClassId === item.schoolClassId,
    );
  }

  protected isCompletedBy(item: Homework, studentId: number): boolean {
    return item.completedStudentIds.includes(studentId);
  }

  protected markComplete(item: Homework, studentId: number): void {
    if (this.isCompletedBy(item, studentId)) {
      return;
    }
    this.homeworkApi.markComplete(item.id, studentId).subscribe({
      next: () => this.reload(),
      error: (err) => this.notify.actionFailed('markComplete failed', err),
    });
  }

  protected openEditHomeworkDialog(item: Homework): void {
    this.openAddHomeworkDialog();
    this.editingHomeworkId.set(item.id);
    this.newSchoolClassId.set(item.schoolClassId);
    this.newStudentGroupId.set(item.studentGroupId);
    this.editingGroupLabel =
      item.studentGroupId !== null ? item.schoolClassName : null;
    this.editingGroupStudentIds.set(item.groupStudentIds ?? []);
    this.newSubjectId = item.subjectId;
    this.selectedAssignmentKey = assignmentKey(
      item.schoolClassId,
      item.studentGroupId,
      item.subjectId,
    );
    this.newText = item.text;
    this.newDueDate = new Date(`${item.dueDate}T00:00:00`);
    this.newRequiresCompletion = item.requiresCompletion;
    this.newTargetStudentIds = [...item.targetStudentIds];
  }

  protected openAddHomeworkDialog(): void {
    this.editingHomeworkId.set(null);
    this.newStudentGroupId.set(null);
    this.editingGroupLabel = null;
    this.editingGroupStudentIds.set([]);
    this.createError.set(null);
    this.fileError.set(null);
    this.selectedAssignmentKey =
      this.teachingAssignmentOptions()[0]?.key ?? null;
    this.newSchoolClassId.set(
      this.anyClass ? (this.schoolClasses()[0]?.id ?? null) : null,
    );
    this.newSubjectId = this.anyClass
      ? (this.givenSubjects()[0]?.id ?? null)
      : null;
    this.onAssignmentChange();
    this.newText = '';
    this.newDueDate = new Date();
    this.newRequiresCompletion = false;
    this.newTargetStudentIds = [];
    this.selectedFile = null;
    this.notify.clear();
    this.homeworkDialogVisible.set(true);
  }

  /** A new homework's dialog opened before its lists arrived gets their
   * first entry once they do, as if opened later. */
  private preselectInOpenDialog(): void {
    if (!this.homeworkDialogVisible() || this.editingHomeworkId() !== null) {
      return;
    }
    if (this.anyClass) {
      if (this.newSchoolClassId() === null) {
        this.newSchoolClassId.set(this.schoolClasses()[0]?.id ?? null);
      }
      this.newSubjectId ??= this.givenSubjects()[0]?.id ?? null;
    } else if (this.selectedAssignmentKey === null) {
      this.selectedAssignmentKey =
        this.teachingAssignmentOptions()[0]?.key ?? null;
      this.onAssignmentChange();
    }
  }

  /** Picking a "Třída a předmět" combo (Teacher mode) sets both ids at once. */
  protected onAssignmentChange(): void {
    const assignment = this.teachingAssignmentOptions().find(
      (a) => a.key === this.selectedAssignmentKey,
    );
    if (assignment) {
      this.newSchoolClassId.set(assignment.schoolClassId);
      this.newStudentGroupId.set(assignment.studentGroupId);
      this.newSubjectId = assignment.subjectId;
    }
    this.newTargetStudentIds = [];
  }

  protected onFileSelected(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0] ?? null;
    this.fileError.set(null);
    if (!file) {
      this.selectedFile = null;
      return;
    }
    if (file.size > MAX_ATTACHMENT_SIZE_BYTES) {
      this.fileError.set('Soubor je příliš velký (max 10 MB).');
      this.selectedFile = null;
      return;
    }
    const extension = file.name.slice(file.name.lastIndexOf('.')).toLowerCase();
    if (!ALLOWED_ATTACHMENT_EXTENSIONS.includes(extension)) {
      this.fileError.set('Nepovolený typ souboru.');
      this.selectedFile = null;
      return;
    }
    this.selectedFile = file;
  }

  protected saveHomework(): void {
    const schoolClassId = this.newSchoolClassId();
    const studentGroupId = this.newStudentGroupId();
    if (
      (!schoolClassId && !studentGroupId) ||
      !this.newSubjectId ||
      !this.newText.trim()
    ) {
      return;
    }
    this.createError.set(null);
    const request: CreateHomeworkRequest = {
      schoolClassId: studentGroupId ? null : schoolClassId,
      studentGroupId,
      subjectId: this.newSubjectId,
      text: this.newText,
      dueDate: this.toDateOnlyString(this.newDueDate),
      requiresCompletion: this.newRequiresCompletion,
      studentIds:
        this.newTargetStudentIds.length > 0
          ? this.newTargetStudentIds
          : undefined,
    };
    const editingId = this.editingHomeworkId();
    const result =
      editingId === null
        ? this.homeworkApi.addHomework(request)
        : this.homeworkApi.updateHomework(editingId, request);
    result.subscribe({
      next: (homework) => this.afterCreate(homework.id),
      error: (err) => {
        console.error('saveHomework failed', err);
        this.createError.set(
          err.status === 403
            ? 'Nemáte oprávnění zadávat úkoly pro tuto třídu a předmět.'
            : editingId === null
              ? 'Zadání úkolu se nezdařilo.'
              : 'Uložení úkolu se nezdařilo.',
        );
      },
    });
  }

  private afterCreate(homeworkId: number): void {
    const file = this.selectedFile;
    if (!file) {
      this.homeworkDialogVisible.set(false);
      this.reload();
      return;
    }
    this.homeworkApi.uploadAttachment(homeworkId, file).subscribe({
      next: () => {
        this.homeworkDialogVisible.set(false);
        this.reload();
      },
      error: (err) => {
        console.error('uploadAttachment failed', err);
        this.notify.warn('Úkol byl zadán, ale příloha se nepodařila nahrát.');
        this.homeworkDialogVisible.set(false);
        this.reload();
      },
    });
  }

  protected deleteHomework(item: Homework): void {
    this.homeworkApi.deleteHomework(item.id).subscribe({
      next: () => this.reload(),
      error: (err) => this.notify.actionFailed('deleteHomework failed', err),
    });
  }

  /** Same blob-download pattern as grades-page.ts's downloadReportCard and
   * messages-page.ts's downloadAttachment. */
  protected downloadAttachment(
    item: Homework,
    attachment: HomeworkAttachment,
  ): void {
    this.homeworkApi.downloadAttachment(item.id, attachment.id).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = attachment.fileName;
        link.click();
        URL.revokeObjectURL(url);
      },
      error: (err) => console.error('downloadAttachment failed', err),
    });
  }

  protected formatFileSize(bytes: number): string {
    if (bytes < 1024) {
      return `${bytes} B`;
    }
    if (bytes < 1024 * 1024) {
      return `${Math.round(bytes / 1024)} KB`;
    }
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }

  /** Local (not UTC) y-m-d — see absences-page.ts's identical helper for
   * why this can't be toISOString().slice(0, 10). */
  private toDateOnlyString(date: Date): string {
    const year = date.getFullYear();
    const month = `${date.getMonth() + 1}`.padStart(2, '0');
    const day = `${date.getDate()}`.padStart(2, '0');
    return `${year}-${month}-${day}`;
  }

  private reload(): void {
    this.homeworkApi.getHomework().subscribe({
      next: (homework) => this.homeworkItems.set(homework),
      error: (err) => {
        console.error('getHomework failed', err);
        this.homeworkItems.update((items) => items ?? []);
      },
    });
  }
}

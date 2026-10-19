import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Button } from '@openng/optimus-ui/button';
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
  Homework,
  HomeworkApiService,
  HomeworkAttachment,
  SchoolClass,
  SchoolClassesApiService,
  Student,
  StudentsApiService,
  Subject,
  SubjectsApiService,
  TeacherTimetableEntry,
  TimetableApiService,
} from '@soovioo/shared';
import { AppIcon } from '../app-icon';

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

@Component({
  selector: 'app-homework-page',
  imports: [
    AppIcon,
    Button,
    Card,
    DatePicker,
    Dialog,
    FormsModule,
    MultiSelect,
    PMessage,
    Select,
    Tag,
    Textarea,
  ],
  templateUrl: './homework-page.html',
})
export class HomeworkPage {
  private readonly homeworkApi = inject(HomeworkApiService);
  private readonly studentsApi = inject(StudentsApiService);
  private readonly subjectsApi = inject(SubjectsApiService);
  private readonly schoolClassesApi = inject(SchoolClassesApiService);
  private readonly timetableApi = inject(TimetableApiService);
  protected readonly authApi = inject(AuthApiService);

  protected readonly homeworkItems = signal<Homework[]>([]);
  protected readonly students = signal<Student[]>([]);
  protected readonly subjects = signal<Subject[]>([]);
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

  protected readonly classOptions = computed(() =>
    this.schoolClasses().map((c) => ({ id: c.id, label: c.name })),
  );
  /** Deduplicated (class, subject) pairs a Teacher actually teaches — the
   * create dialog's single "Třída a předmět" picker for that role. */
  protected readonly teachingAssignmentOptions = computed(() => {
    const seen = new Set<string>();
    const options: {
      key: string;
      label: string;
      schoolClassId: number;
      subjectId: number;
    }[] = [];
    for (const entry of this.myTeachingAssignments()) {
      const key = `${entry.schoolClassId}-${entry.subjectId}`;
      if (seen.has(key)) {
        continue;
      }
      seen.add(key);
      options.push({
        key,
        label: `${entry.schoolClassName} — ${entry.subjectName}`,
        schoolClassId: entry.schoolClassId,
        subjectId: entry.subjectId,
      });
    }
    return options;
  });

  protected readonly homeworkDialogVisible = signal(false);
  protected readonly createError = signal<string | null>(null);
  protected readonly fileError = signal<string | null>(null);
  protected readonly attachmentUploadError = signal<string | null>(null);

  protected selectedAssignmentKey: string | null = null;
  protected readonly newSchoolClassId = signal<number | null>(null);
  protected newSubjectId: number | null = null;
  protected newText = '';
  protected newDueDate = new Date();
  protected newRequiresCompletion = false;
  /** Empty = whole class. */
  protected newTargetStudentIds: number[] = [];
  protected selectedFile: File | null = null;

  protected readonly newClassStudentOptions = computed(() =>
    this.students()
      .filter((s) => s.schoolClassId === this.newSchoolClassId())
      .map((s) => ({ id: s.id, label: `${s.firstName} ${s.lastName}` })),
  );

  constructor() {
    this.reload();
    this.studentsApi.getStudents().subscribe({
      next: (students) => this.students.set(students),
      error: (err) => console.error('getStudents failed', err),
    });
    this.subjectsApi.getSubjects().subscribe({
      next: (subjects) => this.subjects.set(subjects),
      error: (err) => console.error('getSubjects failed', err),
    });
    this.schoolClassesApi.getSchoolClasses().subscribe({
      next: (schoolClasses) => this.schoolClasses.set(schoolClasses),
      error: (err) => console.error('getSchoolClasses failed', err),
    });
    if (this.authApi.isTeacher()) {
      const teacherId = this.authApi.getUserId();
      if (teacherId) {
        this.timetableApi.getTeacherTimetable(teacherId).subscribe({
          next: (entries) => this.myTeachingAssignments.set(entries),
          error: (err) => console.error('getTeacherTimetable failed', err),
        });
      }
    }
  }

  protected canManageHomework(): boolean {
    return this.authApi.isTeacherOrAdmin();
  }

  protected filteredHomework(): Homework[] {
    return this.homeworkItems().filter(
      (h) =>
        (this.filterClassId === null ||
          h.schoolClassId === this.filterClassId) &&
        (this.filterSubjectId === null || h.subjectId === this.filterSubjectId),
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
      error: (err) => console.error('markComplete failed', err),
    });
  }

  protected openAddHomeworkDialog(): void {
    this.createError.set(null);
    this.fileError.set(null);
    this.attachmentUploadError.set(null);
    this.selectedAssignmentKey =
      this.teachingAssignmentOptions()[0]?.key ?? null;
    this.newSchoolClassId.set(
      this.authApi.isAdmin() ? (this.schoolClasses()[0]?.id ?? null) : null,
    );
    this.newSubjectId = this.authApi.isAdmin()
      ? (this.subjects()[0]?.id ?? null)
      : null;
    this.onAssignmentChange();
    this.newText = '';
    this.newDueDate = new Date();
    this.newRequiresCompletion = false;
    this.newTargetStudentIds = [];
    this.selectedFile = null;
    this.homeworkDialogVisible.set(true);
  }

  /** Picking a "Třída a předmět" combo (Teacher mode) sets both ids at once. */
  protected onAssignmentChange(): void {
    const assignment = this.teachingAssignmentOptions().find(
      (a) => a.key === this.selectedAssignmentKey,
    );
    if (assignment) {
      this.newSchoolClassId.set(assignment.schoolClassId);
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
    if (!schoolClassId || !this.newSubjectId || !this.newText.trim()) {
      return;
    }
    this.createError.set(null);
    const request: CreateHomeworkRequest = {
      schoolClassId,
      subjectId: this.newSubjectId,
      text: this.newText,
      dueDate: this.toDateOnlyString(this.newDueDate),
      requiresCompletion: this.newRequiresCompletion,
      studentIds:
        this.newTargetStudentIds.length > 0
          ? this.newTargetStudentIds
          : undefined,
    };
    this.homeworkApi.addHomework(request).subscribe({
      next: (homework) => this.afterCreate(homework.id),
      error: (err) => {
        console.error('addHomework failed', err);
        this.createError.set(
          err.status === 403
            ? 'Nemáte oprávnění zadávat úkoly pro tuto třídu a předmět.'
            : 'Zadání úkolu se nezdařilo.',
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
        this.attachmentUploadError.set(
          'Úkol byl zadán, ale příloha se nepodařila nahrát.',
        );
        this.homeworkDialogVisible.set(false);
        this.reload();
      },
    });
  }

  protected deleteHomework(item: Homework): void {
    this.homeworkApi.deleteHomework(item.id).subscribe({
      next: () => this.reload(),
      error: (err) => console.error('deleteHomework failed', err),
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

  /** "YYYY-MM-DD" (DateOnly on the backend) → "DD.MM.YYYY" — string-only,
   * deliberately not routed through `new Date(...)` so no timezone
   * conversion can ever shift the date by a day. */
  protected formatDueDate(dueDate: string): string {
    const [year, month, day] = dueDate.split('-');
    return `${day}.${month}.${year}`;
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
      error: (err) => console.error('getHomework failed', err),
    });
  }
}

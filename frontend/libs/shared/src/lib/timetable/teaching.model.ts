import { formatNumberWithoutIntl } from '../common/date-format';
import { DayOfWeek } from './timetable-entry.model';

/** Učební plán: hours a week of a subject in a grade (ročník). */
export interface CurriculumItem {
  grade: number;
  subjectId: number;
  hoursPerWeek: number;
  /** In blocks of two; null = as the subject. */
  doubleLessons?: boolean | null;
}

/** A subject in a class: the plan, the timetable and who teaches it. */
export interface TeachingRow {
  subjectId: number;
  subjectName: string;
  subjectAbbreviation: string | null;
  /** By the class's plan; null = not in it, 0 = the class doesn't have it. */
  planHours: number | null;
  /** What the grade's plan says. */
  gradeHours: number | null;
  /** The class has its own hours, not its grade's. */
  classPlan: boolean;
  timetableHours: number;
  teacherId: number | null;
  teacherName: string | null;
  /** Chosen by the system, not by the school. */
  automatic: boolean;
  problems: string[];
  /** In blocks of two: the class's, its grade's or the subject's. */
  doubleLessons: boolean;
  /** What the grade (or the subject) says. */
  gradeDoubleLessons: boolean;
  /** The class has the subject in these groups, not as a whole class. */
  groups: string[];
}

/** A group's plan, the timetable and who teaches it. */
export interface TeachingGroup {
  id: number;
  name: string;
  divisionId: number | null;
  divisionName: string | null;
  subjectId: number;
  subjectName: string;
  subjectAbbreviation: string | null;
  classIds: number[];
  /** "7.A, 7.B". */
  classNames: string;
  /** The ročník its classes share; null = none or several. */
  grade: number | null;
  planHours: number;
  timetableHours: number;
  teacherId: number | null;
  teacherName: string | null;
  automatic: boolean;
  doubleLessons: boolean;
  memberCount: number;
  problems: string[];
  /** `planHours` come from the učební plán of its classes. */
  hoursByPlan: boolean;
}

export interface TeachingClass {
  id: number;
  name: string;
  /** Ročník; null = the plan doesn't apply. */
  grade: number | null;
  problems: string[];
  rows: TeachingRow[];
}

export interface TeachingTeacher {
  id: number;
  name: string;
  /** The aprobace as text. */
  qualification: string | null;
  contractedHours: number | null;
  assignedHours: number;
  timetableHours: number;
  teachingDays: DayOfWeek[];
  /** What the teacher is given, class by class. */
  taught: TeachingTaught[];
  problems: string[];
}

export interface TeachingTaught {
  schoolClassId: number;
  schoolClassName: string;
  subjectId: number;
  subjectName: string;
  subjectAbbreviation: string | null;
  hours: number;
  automatic: boolean;
  /** Of a group: `schoolClassName` is the group's name. */
  studentGroupId?: number | null;
}

/** One teacher's přidělení in a version. */
export interface TeacherTeaching {
  versionId: number;
  versionName: string;
  teacher: TeachingTeacher;
}

/** "6.A M 4 h, 6.B M 4 h". */
export function taughtSummary(taught: TeachingTaught[]): string {
  return taught
    .map(
      (t) =>
        `${t.schoolClassName} ${t.subjectAbbreviation ?? t.subjectName} ${formatHours(t.hours)}`,
    )
    .join(', ');
}

/** Učební plán and přidělení výuky of one timetable version. */
export interface TeachingOverview {
  versionId: number;
  canEdit: boolean;
  /** Ročníky of the school's classes and of the plan. */
  grades: number[];
  curriculum: CurriculumItem[];
  classes: TeachingClass[];
  teachers: TeachingTeacher[];
  groups: TeachingGroup[];
  /** A draft: only here the plan can be filled by the RVPs. */
  isDraft: boolean;
  /** The plan against the RVPs of the classes; null = not for this user. */
  rvp: RvpCheck | null;
}

/** An RVP a class can be taught by. */
export interface RvpProgramOption {
  code: string;
  name: string;
  family: 'ZV' | 'G' | 'SOV';
}

export interface RvpCheck {
  programs: RvpProgramCheck[];
  /** Classes with no program, not checked. */
  classesWithout: string[];
}

export interface RvpProgramCheck {
  code: string;
  name: string;
  classes: string[];
  stages: RvpStageCheck[];
  problems: string[];
  subjects: RvpSubject[];
  areas: { key: string; name: string }[];
}

export interface RvpStageCheck {
  label: string;
  fromGrade: number;
  toGrade: number;
  /** No class of the program there: totals and areas aren't checked. */
  missingGrades: number[];
  total: number;
  have: number;
  disponible: number | null;
  /** Hours over the minima; null = not known. */
  disponibleUsed: number | null;
  areas: RvpAreaCheck[];
}

export interface RvpAreaCheck {
  key: string;
  name: string;
  min: number;
  have: number;
  ok: boolean;
}

export interface RvpSubject {
  subjectId: number;
  subjectName: string;
  /** null = only towards the disponibilní hours. */
  areaKey: string | null;
  /** The school chose, the system didn't. */
  chosen: boolean;
}

export interface RvpFillOptions {
  overwrite: boolean;
  createSubjects: boolean;
  spreadDisponible: boolean;
  removeOthers: boolean;
}

export interface RvpFillSummary {
  added: number;
  overwritten: number;
  kept: number;
  removed: number;
  subjectsCreated: string[];
  subjectsMissing: string[];
  classesSkipped: string[];
  notes: string[];
}

export interface FillCurriculumResponse {
  summary: RvpFillSummary;
  /** null in a dry run. */
  overview: TeachingOverview | null;
}

/** Dělení: groups of the same classes at the same time. */
export interface GroupDivision {
  id: number;
  name: string;
  /** null = as the učební plán of its classes has the row it covers, else its groups' subjects. */
  hoursPerWeek: number | null;
  doubleLessons: boolean | null;
  classIds: number[];
  /** The row of the učební plán it teaches ("Povinně volitelný předmět",
   * "Další cizí jazyk") its groups' subjects are chosen for; null = their own rows. */
  coversSubjectId: number | null;
}

/** Pupils taught a subject apart from the rest of their class. */
export interface StudentGroup {
  id: number;
  name: string;
  divisionId: number | null;
  subjectId: number;
  /** null = as the učební plán of its classes; in a division the division's. */
  hoursPerWeek: number | null;
  doubleLessons: boolean | null;
  /** null = chosen automatically. */
  teacherId: number | null;
  /** null = from the subject's classrooms. */
  classroomId: number | null;
  /** In a division the division's. */
  classIds: number[];
  studentIds: number[];
}

export interface StudentGroups {
  divisions: GroupDivision[];
  groups: StudentGroup[];
}

export type SaveGroupDivisionRequest = Omit<GroupDivision, 'id'>;

export type SaveStudentGroupRequest = Omit<StudentGroup, 'id'>;

/** "1 h", "1,5 h". */
export function formatHours(hours: number): string {
  return `${formatNumberWithoutIntl(hours)} h`;
}

const DAY_SHORT: Record<DayOfWeek, string> = {
  Monday: 'Po',
  Tuesday: 'Út',
  Wednesday: 'St',
  Thursday: 'Čt',
  Friday: 'Pá',
  Saturday: 'So',
  Sunday: 'Ne',
};
const WEEKDAYS: DayOfWeek[] = [
  'Monday',
  'Tuesday',
  'Wednesday',
  'Thursday',
  'Friday',
];

/** "Po–Pá", "Po, St, Pá". */
export function teachingDaysLabel(days: DayOfWeek[]): string {
  if (days.length === 5 && WEEKDAYS.every((d) => days.includes(d))) {
    return 'Po–Pá';
  }
  return (
    (Object.keys(DAY_SHORT) as DayOfWeek[])
      .filter((d) => days.includes(d))
      .map((d) => DAY_SHORT[d])
      .join(', ') || '—'
  );
}

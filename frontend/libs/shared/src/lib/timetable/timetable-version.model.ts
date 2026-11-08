import { DayOfWeek, WeekParity } from './timetable-entry.model';

export type TimetableVersionStatus = 'Draft' | 'Published';

export interface TimetableVersion {
  id: number;
  name: string;
  /** "yyyy-MM-dd" */
  validFrom: string;
  /** Set on a published version that a later one follows; otherwise valid
   * until further notice. */
  validTo: string | null;
  status: TimetableVersionStatus;
  entryCount: number;
  createdAt: string;
  publishedAt: string | null;
  /** Null for an empty one, or once the source (a draft) is deleted… */
  copiedFromVersionId: number | null;
  /** …when its name at the time of copying stays. */
  copiedFromName: string | null;
}

export interface CreateTimetableVersionRequest {
  name: string;
  validFrom: string;
  /** Null = an empty draft. */
  copyFromVersionId: number | null;
}

export interface UpdateTimetableVersionRequest {
  name: string;
  validFrom: string;
}

/** The published version valid on `date` ("yyyy-MM-dd"), as the server
 * decides it. */
export function timetableVersionOn(
  versions: TimetableVersion[],
  date: string,
): TimetableVersion | undefined {
  return versions.find(
    (v) =>
      v.status === 'Published' &&
      v.validFrom <= date &&
      (v.validTo === null || v.validTo >= date),
  );
}

/** Fill: keep the lessons, add what the plan misses. All: keep only the
 * locked lessons. Update: bring the lessons in line with the plan, the
 * přidělení and the teachers' availability and wishes. */
export type TimetableGenerationMode = 'Fill' | 'All' | 'Update';

/** With Update. Fewest: any unlocked lesson can move, each move costing.
 * AffectedOnly: only lessons the changes concern move. */
export type TimetableUpdateMoves = 'Fewest' | 'AffectedOnly';

/** With Update, lessons at odds with the plan or the přidělení. FollowPlan:
 * the plan decides. Leave: they stay and the result lists them. */
export type TimetableUpdateMismatches = 'FollowPlan' | 'Leave';

/** Blocked: it can't go there. Unsuitable: it can, against a wish or leaving
 * a gap. */
export interface TimetableDropSlot {
  day: DayOfWeek;
  period: number;
  status: 'Free' | 'Unsuitable' | 'Blocked';
  reasons: string[];
}

/** A lesson the plan wants and the timetable lacks. */
export interface UnplacedLesson {
  subjectId: number;
  subjectShort: string;
  subjectName: string;
  /** Of a division its first group. */
  groupId: number | null;
  groupName: string | null;
  teacherName: string | null;
  weekParity: WeekParity;
}

export interface TimetableAssessment {
  /** How it looks: gaps, late starts, wishes not met. */
  notes: string[];
  /** What's wrong with it, as checked before publishing. */
  problems: string[];
}

export type TimetableGenerationStatus =
  'Queued' | 'Running' | 'Done' | 'Failed';

export interface TimetableGeneration {
  id: number;
  mode: TimetableGenerationMode;
  status: TimetableGenerationStatus;
  createdAt: string;
  startedAt: string | null;
  finishedAt: string | null;
  /** Lessons added; of an update, lessons changed. */
  placed: number;
  /** What didn't fit or couldn't be done — to fix before generating again. */
  messages: string[];
  /** How the result looks: class gaps, teachers' gaps… */
  notes: string[];
  /** The best timetable so far while it runs. */
  progress: TimetableGenerationProgress | null;
  /** While queued: runs (of every school) before this one… */
  queueAhead: number;
  /** …and the estimated wait for them. */
  waitSeconds: number;
}

export interface TimetableGenerationProgress {
  /** Requirements = looking for a timetable that meets them, Polish =
   * cutting teachers' gaps. */
  phase: 'Requirements' | 'Polish';
  /** Lessons placed out of `total`. */
  lessons: number;
  total: number;
  withoutRoom: number;
  classGaps: number;
  /** Periods of classes' main blocks without a lesson for all pupils. */
  mainBlockGaps: number;
  teacherGaps: number;
  /** 0–1 of the polish's time (it ends at its limit or when it stalls). */
  polishShare: number;
}

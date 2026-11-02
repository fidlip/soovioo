export type TimetableVersionStatus = 'Draft' | 'Published' | 'Archived';

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

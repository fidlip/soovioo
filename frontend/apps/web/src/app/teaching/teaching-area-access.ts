import { AuthApiService } from '@soovioo/shared';

/** Who gets anywhere in Administrace → Výuka. */
export function canSeeTeachingArea(auth: AuthApiService): boolean {
  return (
    auth.can('TeachingRead') ||
    auth.can('Teaching') ||
    auth.can('Timetable') ||
    auth.can('TimetableVersionsRead')
  );
}

/** Minimal name-only teacher record for pickers (e.g. timetable entry
 * assignment), fetched by any authenticated school member — unlike the full
 * `AppUser` from `UsersApiService`, which is Admin-only. */
export interface Teacher {
  id: number;
  username: string;
}

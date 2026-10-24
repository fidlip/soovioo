/** Minimal name-only parent record for pickers (e.g. the parent↔student link
 * form), fetched by any authenticated school member — unlike the full
 * `AppUser` from `UsersApiService`, which is Admin-only. */
export interface Parent {
  id: number;
  username: string;
  /** Display name ("Petr Novák", or the username). */
  name: string;
  /** Photo token for `PhotosApiService` URLs — present only when the
   * caller may see the photo. */
  photo?: string | null;
}

import { Person, PhotoSize, PhotosApiService } from '@soovioo/shared';

/** The person's photo — a pupil's under their pupil record, anyone else's
 * under their account (someone without one has none). */
export function personPhotoUrl(
  photosApi: PhotosApiService,
  person: Person,
  size?: PhotoSize,
): string | null {
  if (!person.photo) {
    return null;
  }
  if (person.student) {
    return photosApi.studentPhotoUrl(person.student.id, person.photo, size);
  }
  return person.account
    ? photosApi.userPhotoUrl(person.account.userId, person.photo, size)
    : null;
}

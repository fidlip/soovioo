/** "Viditelnost údajů", see backend `People/Visibility.cs`. Whose data
 * a setting is about — a teacher who is also a parent decides both. */
export type VisibilityOwner = 'Staff' | 'Parent' | 'Student';

/** `Name` = being on the list at all; the other fields never reach
 * further than the name. `Nickname` only decides whether the nickname
 * shows instead of the name. */
export type VisibilityField = 'Name' | 'Phone' | 'Email' | 'Photo' | 'Nickname';

/** Who looks. Staff are an audience only for a pupil's nickname
 * (`Teachers`) — otherwise that's a matter of permissions. */
export type VisibilityAudience = 'Parents' | 'Students' | 'Teachers';

/** Ordered, a wider scope includes the narrower ones. */
export type VisibilityScope = 'Nobody' | 'MyClasses' | 'School';

export const VISIBILITY_SCOPES: VisibilityScope[] = [
  'Nobody',
  'MyClasses',
  'School',
];

/** The school's range for one setting: individuals choose between `min`
 * and `max`, `default` applies until they do. */
export interface VisibilityRule {
  owner: VisibilityOwner;
  field: VisibilityField;
  audience: VisibilityAudience;
  min: VisibilityScope;
  default: VisibilityScope;
  max: VisibilityScope;
}

export interface VisibilityRules {
  rules: VisibilityRule[];
  /** Pupils' photos are on at all (each still needs a guardian's consent). */
  studentPhotos: boolean;
  /** Parents may upload a photo of themselves. */
  parentPhotos: boolean;
  /** Teachers see pupils' nicknames until they choose themselves. */
  staffSeeStudentNicknames: boolean;
}

/** One of someone's settings as offered next to their data. */
export interface VisibilitySetting extends VisibilityRule {
  /** What applies now — the choice or the default, within the school's
   * range and the name's scope. */
  scope: VisibilityScope;
  /** Chosen by the person (vs. the school's default). */
  custom: boolean;
  editable: boolean;
}

export interface SetVisibilityRequest {
  owner: VisibilityOwner;
  field: VisibilityField;
  audience: VisibilityAudience;
  scope: VisibilityScope;
}

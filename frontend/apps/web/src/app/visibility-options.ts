import {
  VISIBILITY_SCOPES,
  VisibilityAudience,
  VisibilityField,
  VisibilityOwner,
  VisibilityScope,
} from '@soovioo/shared';

export const VISIBILITY_FIELDS: VisibilityField[] = [
  'Name',
  'Phone',
  'Email',
  'Photo',
];

export const VISIBILITY_FIELD_LABELS: Record<VisibilityField, string> = {
  Name: 'Jméno (v seznamu)',
  Phone: 'Telefon',
  Email: 'E-mail',
  Photo: 'Fotka',
};

export const VISIBILITY_AUDIENCE_LABELS: Record<VisibilityAudience, string> = {
  Parents: 'Rodiče',
  Students: 'Žáci',
};

export const VISIBILITY_OWNER_LABELS: Record<VisibilityOwner, string> = {
  Staff: 'Zaměstnanci',
  Parent: 'Rodiče',
  Student: 'Žáci',
};

/** "Jako zaměstnanec" — for accounts deciding as more than one owner. */
export const VISIBILITY_OWNER_ROLE_LABELS: Record<VisibilityOwner, string> = {
  Staff: 'Jako zaměstnanec',
  Parent: 'Jako rodič',
  Student: 'Jako žák',
};

export const VISIBILITY_SCOPE_LABELS: Record<VisibilityScope, string> = {
  Nobody: 'Nikdo',
  MyClasses: 'Moje třídy',
  School: 'Celá škola',
};

/** The short form next to a field. */
export const VISIBILITY_SCOPE_SHORT: Record<VisibilityScope, string> = {
  Nobody: 'Nikdo',
  MyClasses: 'Třídy',
  School: 'Škola',
};

export const VISIBILITY_SCOPE_ICONS: Record<VisibilityScope, string> = {
  Nobody: 'pi pi-lock',
  MyClasses: 'pi pi-users',
  School: 'pi pi-building',
};

export function scopeRank(scope: VisibilityScope): number {
  return VISIBILITY_SCOPES.indexOf(scope);
}

export function widestScope(scopes: VisibilityScope[]): VisibilityScope {
  return scopes.reduce<VisibilityScope>(
    (widest, s) => (scopeRank(s) > scopeRank(widest) ? s : widest),
    'Nobody',
  );
}

/** Scopes between `min` and `max`. */
export function scopesBetween(
  min: VisibilityScope,
  max: VisibilityScope,
): VisibilityScope[] {
  return VISIBILITY_SCOPES.filter(
    (s) => scopeRank(s) >= scopeRank(min) && scopeRank(s) <= scopeRank(max),
  );
}

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
  'Nickname',
];

export const VISIBILITY_FIELD_LABELS: Record<VisibilityField, string> = {
  Name: 'Jméno (v seznamu)',
  Phone: 'Telefon',
  Email: 'E-mail',
  Photo: 'Fotka',
  Nickname: 'Přezdívka',
};

export const VISIBILITY_AUDIENCE_LABELS: Record<VisibilityAudience, string> = {
  Parents: 'Rodiče',
  Students: 'Žáci',
  Teachers: 'Učitelé',
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

/** A pupil's nickname for teachers, a parent's for their children — their
 * scopes read differently. */
const NICKNAME_SCOPE_LABELS: Partial<
  Record<VisibilityAudience, Partial<Record<VisibilityScope, string>>>
> = {
  Teachers: { MyClasses: 'Kdo žáka učí', School: 'Všichni učitelé' },
  Students: { MyClasses: 'Moje děti' },
};
const NICKNAME_SCOPE_SHORT: Partial<
  Record<VisibilityAudience, Partial<Record<VisibilityScope, string>>>
> = {
  Teachers: { MyClasses: 'Učitelé', School: 'Všichni' },
  Students: { MyClasses: 'Děti' },
};

interface VisibilityKey {
  field: VisibilityField;
  audience: VisibilityAudience;
}

export function scopeLabel(key: VisibilityKey, scope: VisibilityScope): string {
  return (
    (key.field === 'Nickname'
      ? NICKNAME_SCOPE_LABELS[key.audience]?.[scope]
      : undefined) ?? VISIBILITY_SCOPE_LABELS[scope]
  );
}

export function scopeShort(key: VisibilityKey, scope: VisibilityScope): string {
  return (
    (key.field === 'Nickname'
      ? NICKNAME_SCOPE_SHORT[key.audience]?.[scope]
      : undefined) ?? VISIBILITY_SCOPE_SHORT[scope]
  );
}

/** A parent's nickname never reaches further than their own children. */
export function widestScopeFor(key: VisibilityKey): VisibilityScope {
  return key.field === 'Nickname' && key.audience === 'Students'
    ? 'MyClasses'
    : 'School';
}

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

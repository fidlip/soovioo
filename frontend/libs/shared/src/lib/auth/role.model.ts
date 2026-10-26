/** What sort of person a role is for (fixed once the role exists). */
export type RoleKind = 'Staff' | 'Parent' | 'Student';

/** How far a permission reaches — see `PermissionScope` on the backend. */
export type PermissionScope =
  'None' | 'Own' | 'ClassTeacher' | 'MyClasses' | 'School';

/** The backend's permission catalog (`Permission`). */
export type Permission =
  | 'GradesRead'
  | 'GradesWrite'
  | 'AbsencesRead'
  | 'AbsencesWrite'
  | 'AbsencesExcuse'
  | 'BehaviorRead'
  | 'BehaviorPraise'
  | 'BehaviorClassTeacher'
  | 'BehaviorDirector'
  | 'HomeworkRead'
  | 'HomeworkWrite'
  | 'HomeworkComplete'
  | 'EventsWrite'
  | 'PaymentsRead'
  | 'PaymentsWrite'
  | 'MessagesBroadcastClass'
  | 'MessagesBroadcastSchool'
  | 'Timetable'
  | 'Classes'
  | 'Subjects'
  | 'Classrooms'
  | 'Students'
  | 'StudentPhotos'
  | 'Retention'
  | 'MatrikaExport'
  | 'AuditLog'
  | 'Administration';

export type BuiltInRole = 'Admin' | 'Teacher' | 'Parent' | 'Student';

/** A role as accounts and the role switcher refer to it. */
export interface RoleRef {
  id: number;
  name: string;
  kind: RoleKind;
  /** Set for the four roles every school has. */
  builtIn?: BuiltInRole | null;
}

/** Administrace → Role. */
export interface Role extends RoleRef {
  userCount: number;
  permissions: Record<Permission, PermissionScope>;
}

export interface PermissionInfo {
  permission: Permission;
  agenda: string;
  label: string;
  staffScopes: PermissionScope[];
  parentScopes: PermissionScope[];
  studentScopes: PermissionScope[];
}

export const ROLE_KIND_LABELS: Record<RoleKind, string> = {
  Staff: 'Zaměstnanec',
  Parent: 'Rodič',
  Student: 'Žák',
};

/** Read as "whose": "Vidět známky: Kde učí". */
const SCOPE_LABELS: Record<PermissionScope, string> = {
  None: '—',
  Own: 'Své',
  ClassTeacher: 'Své třídy',
  MyClasses: 'Kde učí',
  School: 'Celé školy',
};

export function scopeLabel(scope: PermissionScope, kind: RoleKind): string {
  return scope === 'Own' && kind === 'Parent'
    ? 'Svých dětí'
    : SCOPE_LABELS[scope];
}

export function scopesFor(
  info: PermissionInfo,
  kind: RoleKind,
): PermissionScope[] {
  return kind === 'Staff'
    ? info.staffScopes
    : kind === 'Parent'
      ? info.parentScopes
      : info.studentScopes;
}

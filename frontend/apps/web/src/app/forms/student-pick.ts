import { nameText, NameLabel } from '@soovioo/shared';
export interface PickableStudent {
  id: number;
  firstName: string;
  lastName: string;
  schoolClassId: number;
  schoolClassName: string;
  label?: NameLabel | null;
}

export interface StudentPickOption {
  id: number;
  label: string;
}

/** The classes the students are in — options of the class that narrows the
 * student list. */
export function studentClasses(
  students: readonly PickableStudent[],
): { id: number; name: string }[] {
  const byId = new Map(
    students.map((s) => [s.schoolClassId, s.schoolClassName]),
  );
  return [...byId]
    .map(([id, name]) => ({ id, name }))
    .sort((a, b) => a.name.localeCompare(b.name, 'cs', { numeric: true }));
}

/** Students of the class (all of them without one), plus the ones already
 * picked from other classes, whose chips need their names. */
export function studentPickOptions(
  students: readonly PickableStudent[],
  classId: number | null,
  selectedIds: readonly number[],
): StudentPickOption[] {
  return students
    .filter(
      (s) =>
        classId === null ||
        s.schoolClassId === classId ||
        selectedIds.includes(s.id),
    )
    .map((s) => ({
      id: s.id,
      label: `${nameText(`${s.firstName} ${s.lastName}`, s.label)} (${s.schoolClassName})`,
    }));
}

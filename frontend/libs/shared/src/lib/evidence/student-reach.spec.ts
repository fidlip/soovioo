import {
  reachedSubjectIds,
  reachesStudent,
  StudentReach,
} from './student-reach';

describe('student reach', () => {
  const teacher: StudentReach = {
    all: false,
    students: [
      // Maths to the whole class, German in a group.
      { studentId: 1, subjectIds: [10, 20], studentGroupIds: [5] },
      // Maths only — not in the German group.
      { studentId: 2, subjectIds: [10], studentGroupIds: [] },
      // The class teacher's pupil, no lesson.
      { studentId: 3, subjectIds: [], studentGroupIds: [] },
    ],
  };

  it('reaches every pupil in any subject for the whole school', () => {
    const all: StudentReach = { all: true, students: [] };
    expect(reachesStudent(all, 99, 10)).toBe(true);
    expect(reachedSubjectIds(all)).toBeNull();
  });

  it('reaches a group subject only for its pupils', () => {
    expect(reachesStudent(teacher, 1, 20)).toBe(true);
    expect(reachesStudent(teacher, 2, 20)).toBe(false);
    expect(reachesStudent(teacher, 2, 10)).toBe(true);
  });

  it('reaches a pupil without subjects when no subject is asked', () => {
    expect(reachesStudent(teacher, 3)).toBe(true);
    expect(reachesStudent(teacher, 3, 10)).toBe(false);
    expect(reachesStudent(teacher, 4)).toBe(false);
  });

  it('collects the subjects reached, any when a pupil has any', () => {
    expect([...(reachedSubjectIds(teacher) ?? [])].sort()).toEqual([10, 20]);
    expect(
      reachedSubjectIds({
        all: false,
        students: [{ studentId: 1, subjectIds: null, studentGroupIds: [] }],
      }),
    ).toBeNull();
  });
});

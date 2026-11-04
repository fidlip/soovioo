import { Classroom } from './classroom.model';
import { Subject, subjectClassroomIds } from './subject.model';

describe('subjectClassroomIds', () => {
  const room = (id: number, type: Classroom['type']): Classroom => ({
    id,
    name: `${id}`,
    shortName: null,
    type,
    capacity: null,
  });
  const classrooms = [
    room(1, 'Homeroom'),
    room(2, 'Homeroom'),
    room(3, 'Gym'),
    room(4, 'Gym'),
  ];
  const subject = (items: Partial<Subject['classrooms'][number]>[]): Subject =>
    ({
      classrooms: items.map((c, position) => ({
        position,
        classroomId: null,
        type: null,
        ...c,
      })),
    }) as Subject;

  it('a subject without a list has the class’s homeroom', () => {
    expect(subjectClassroomIds(subject([]), classrooms, 2)).toEqual([2]);
    expect(subjectClassroomIds(subject([]), classrooms, null)).toEqual([]);
  });

  it('no subject picked has none', () => {
    expect(subjectClassroomIds(undefined, classrooms, 2)).toEqual([]);
  });

  it('keeps the order and expands a type to its classrooms', () => {
    const list = subject([{ classroomId: 4 }, { type: 'Gym' }]);
    expect(subjectClassroomIds(list, classrooms, 2)).toEqual([4, 3]);
  });

  it('any homeroom means the class’s own, none without one', () => {
    const list = subject([{ classroomId: 3 }, { type: 'Homeroom' }]);
    expect(subjectClassroomIds(list, classrooms, 2)).toEqual([3, 2]);
    expect(subjectClassroomIds(list, classrooms, null)).toEqual([3]);
  });
});

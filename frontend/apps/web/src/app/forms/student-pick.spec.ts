import { studentClasses, studentPickOptions } from './student-pick';

const students = [
  {
    id: 1,
    firstName: 'Jan',
    lastName: 'Novák',
    schoolClassId: 10,
    schoolClassName: '10.A',
  },
  {
    id: 2,
    firstName: 'Eva',
    lastName: 'Malá',
    schoolClassId: 5,
    schoolClassName: '5.A',
  },
  {
    id: 3,
    firstName: 'Petr',
    lastName: 'Král',
    schoolClassId: 5,
    schoolClassName: '5.A',
  },
];

describe('studentClasses', () => {
  it('lists each class once, ordered by name with numbers compared as numbers', () => {
    expect(studentClasses(students)).toEqual([
      { id: 5, name: '5.A' },
      { id: 10, name: '10.A' },
    ]);
  });
});

describe('studentPickOptions', () => {
  it('offers everyone without a class', () => {
    expect(studentPickOptions(students, null, []).map((o) => o.id)).toEqual([
      1, 2, 3,
    ]);
  });

  it('offers the class, plus those already picked from elsewhere', () => {
    expect(studentPickOptions(students, 5, [1]).map((o) => o.id)).toEqual([
      1, 2, 3,
    ]);
    expect(studentPickOptions(students, 10, []).map((o) => o.id)).toEqual([1]);
  });

  it('names the student with the class', () => {
    expect(studentPickOptions(students, 10, [])[0].label).toBe(
      'Jan Novák (10.A)',
    );
  });
});

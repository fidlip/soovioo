import { qualifiedFirst, qualifiedFor } from './teacher.model';

describe('qualifiedFor', () => {
  const maths = 1;
  const history = 2;

  it('covers the grades of the range, either end open', () => {
    const q = [{ subjectId: maths, gradeFrom: 6, gradeTo: 9 }];
    expect([5, 6, 9, 10].map((g) => qualifiedFor(q, maths, g))).toEqual([
      false,
      true,
      true,
      false,
    ]);
    const from = [{ subjectId: maths, gradeFrom: 6, gradeTo: null }];
    expect(qualifiedFor(from, maths, 9)).toBe(true);
    expect(qualifiedFor(from, maths, 5)).toBe(false);
  });

  it('covers a class without a ročník by any range', () => {
    const q = [{ subjectId: maths, gradeFrom: 6, gradeTo: 9 }];
    expect(qualifiedFor(q, maths, null)).toBe(true);
  });

  it('needs the subject', () => {
    const q = [{ subjectId: maths, gradeFrom: null, gradeTo: null }];
    expect(qualifiedFor(q, history, 5)).toBe(false);
    expect(qualifiedFor([], maths, null)).toBe(false);
  });
});

describe('qualifiedFirst', () => {
  it('puts the qualified first, marked, otherwise keeping the order', () => {
    const options = [
      { id: 1, label: 'Adam' },
      { id: 2, label: 'Bára' },
      { id: 3, label: 'Cyril' },
      { id: 4, label: 'Dana' },
    ];
    expect(qualifiedFirst(options, (o) => o.id === 2 || o.id === 4)).toEqual([
      { id: 2, label: 'Bára (aprobace)' },
      { id: 4, label: 'Dana (aprobace)' },
      { id: 1, label: 'Adam' },
      { id: 3, label: 'Cyril' },
    ]);
    expect(options[1].label).toBe('Bára');
  });
});

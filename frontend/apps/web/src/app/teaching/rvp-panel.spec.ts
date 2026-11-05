import { rvpAreaChoice } from './rvp-panel';

const subject = { subjectId: 1, subjectName: 'Matematika' };

describe('rvpAreaChoice', () => {
  it('is automatic until the school chooses, whatever was recognised', () => {
    expect(
      rvpAreaChoice({ ...subject, areaKey: 'matematika', chosen: false }),
    ).toBe('automatic');
    expect(rvpAreaChoice({ ...subject, areaKey: null, chosen: false })).toBe(
      'automatic',
    );
  });

  it('is the chosen area, or none for disponibilní hours only', () => {
    expect(
      rvpAreaChoice({ ...subject, areaKey: 'matematika', chosen: true }),
    ).toBe('matematika');
    expect(rvpAreaChoice({ ...subject, areaKey: null, chosen: true })).toBe(
      'none',
    );
  });
});

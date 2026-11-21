import { ClassBookDay, ClassBookLesson } from '@soovioo/shared';
import {
  classBookDay,
  lessonTime,
  mondayOf,
  nextSchoolDay,
  weekRows,
} from './class-book';

describe('class book days', () => {
  it('skips the weekend', () => {
    const friday = new Date(2026, 10, 6);
    expect(nextSchoolDay(friday, 1).getDate()).toBe(9);
    expect(nextSchoolDay(new Date(2026, 10, 9), -1).getDate()).toBe(6);
  });

  it('opens the given day, else today, at the weekend the Friday before', () => {
    expect(classBookDay('2026-11-04').getDate()).toBe(4);
    expect(classBookDay(null, new Date(2026, 10, 4, 10)).getDate()).toBe(4);
    expect(classBookDay('nonsense', new Date(2026, 10, 8, 10)).getDate()).toBe(
      6,
    );
  });

  it('shows the lesson time without seconds and leading zeros', () => {
    const lesson = {
      startTime: '08:00:00',
      endTime: '08:45:00',
    } as ClassBookLesson;
    expect(lessonTime(lesson)).toBe('8:00–8:45');
    expect(lessonTime({ ...lesson, startTime: null })).toBe('');
  });

  it('starts a week on its Monday', () => {
    expect(mondayOf(new Date(2026, 10, 19)).getDate()).toBe(16);
    expect(mondayOf(new Date(2026, 10, 16)).getDate()).toBe(16);
    expect(mondayOf(new Date(2026, 10, 22)).getDate()).toBe(16);
  });

  it('lays the week out by the periods any day has', () => {
    const lesson = (periodNumber: number, who: string) =>
      ({ periodNumber, who }) as ClassBookLesson;
    const day = (lessons: ClassBookLesson[]) => ({ lessons }) as ClassBookDay;
    const rows = weekRows([
      day([lesson(1, 'a'), lesson(3, 'b'), lesson(3, 'c')]),
      day([lesson(2, 'd')]),
    ]);
    expect(rows.map((r) => r.period)).toEqual([1, 2, 3]);
    expect(rows[2].cells.map((c) => c.map((l) => l.who))).toEqual([
      ['b', 'c'],
      [],
    ]);
  });
});

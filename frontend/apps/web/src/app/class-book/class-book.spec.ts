import { ClassBookLesson } from '@soovioo/shared';
import { classBookDay, lessonTime, nextSchoolDay } from './class-book';

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
});

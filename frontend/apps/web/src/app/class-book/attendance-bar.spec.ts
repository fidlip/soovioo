import { ClassBookLesson, LessonMark } from '@soovioo/shared';
import { barLabel, barSegments } from './attendance-bar';

describe('attendance bar', () => {
  const mark = (status: LessonMark['status']) =>
    ({ status, studentId: 1, studentName: '', minutes: null }) as LessonMark;
  const lesson = (marks: LessonMark[], attendanceTaken = true) =>
    ({ attendanceTaken, pupilCount: 5, marks }) as unknown as ClassBookLesson;

  it('has a segment a pupil, the present first', () => {
    expect(
      barSegments(lesson([mark('Absent'), mark('Late'), mark('Away')])),
    ).toEqual(['Present', 'Present', 'Late', 'Away', 'Absent']);
    expect(barLabel(lesson([mark('Absent'), mark('Absent')]))).toBe(
      'přítomno 3, chybí 2',
    );
  });

  it('is grey while the attendance is not taken', () => {
    expect(barSegments(lesson([], false))).toBeNull();
    expect(barLabel(lesson([], false))).toBe('Docházka nezapsána');
  });
});

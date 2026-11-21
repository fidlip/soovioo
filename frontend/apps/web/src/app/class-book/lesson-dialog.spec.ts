import { ClassBookLesson, LessonPupils } from '@soovioo/shared';
import { attendanceGroups } from './class-book';
import { isOn } from './current-lesson';
import { initialMarks, markRequests } from './lesson-dialog';

const pupil = (
  studentId: number,
  status: LessonPupils['pupils'][0]['status'] = null,
  suggestion: string | null = null,
) => ({
  studentId,
  firstName: 'X',
  lastName: 'Y',
  photo: null,
  status,
  minutes: status === 'Late' ? 5 : null,
  suggestion,
});

describe('lesson attendance', () => {
  it('starts with everyone there but the likely away, before it is taken', () => {
    const marks = initialMarks({
      attendanceTaken: false,
      pupils: [pupil(1), pupil(2, null, 'Omluvenka: nemoc')],
    });
    expect(marks[1].state).toBe('Present');
    expect(marks[2].state).toBe('Absent');
  });

  it('starts as saved once taken, suggestions aside', () => {
    const marks = initialMarks({
      attendanceTaken: true,
      pupils: [pupil(1, 'Late'), pupil(2, null, 'Omluvenka: nemoc')],
    });
    expect(marks[1]).toEqual({ state: 'Late', minutes: 5 });
    expect(marks[2].state).toBe('Present');
  });

  it('sends only the pupils not there, minutes only for coming late or leaving', () => {
    expect(
      markRequests({
        1: { state: 'Present', minutes: null },
        2: { state: 'Absent', minutes: 7 },
        3: { state: 'LeftEarly', minutes: 10 },
      }),
    ).toEqual([
      { studentId: 2, status: 'Absent', minutes: null },
      { studentId: 3, status: 'LeftEarly', minutes: 10 },
    ]);
  });

  it('sums the attendance up by state', () => {
    const lesson = {
      marks: [
        {
          studentId: 1,
          studentName: 'Eva Bílá',
          status: 'Absent',
          minutes: null,
        },
        { studentId: 2, studentName: 'Jan Adámek', status: 'Late', minutes: 5 },
        {
          studentId: 3,
          studentName: 'Petr Cibulka',
          status: 'Absent',
          minutes: null,
        },
      ],
    } as ClassBookLesson;
    expect(
      attendanceGroups(lesson).map((g) => [
        g.label,
        g.marks.map((m) => m.studentName),
      ]),
    ).toEqual([
      ['Chybí', ['Eva Bílá', 'Petr Cibulka']],
      ['Pozdě', ['Jan Adámek']],
    ]);
    expect(attendanceGroups({ ...lesson, marks: [] })).toEqual([]);
  });

  it('knows a lesson going on', () => {
    const lesson = {
      startTime: '08:00:00',
      endTime: '08:45:00',
    } as ClassBookLesson;
    expect(isOn(lesson, new Date(2026, 10, 2, 8, 10))).toBe(true);
    expect(isOn(lesson, new Date(2026, 10, 2, 8, 45))).toBe(false);
    expect(isOn(lesson, new Date(2026, 10, 2, 7, 59))).toBe(false);
  });
});

import { localizeStudentTerm } from './student-term';

describe('localizeStudentTerm', () => {
  it.each([
    ['Žák: Jan Novák', 'Secondary', 'Student: Jan Novák'],
    ['Všichni žáci', 'Secondary', 'Všichni studenti'],
    ['Hledat žáka', 'Secondary', 'Hledat studenta'],
    [
      'Odesláno 3 žákům ve třídě.',
      'Secondary',
      'Odesláno 3 studentům ve třídě.',
    ],
    ['Žádní studenti.', 'Elementary', 'Žádní žáci.'],
    ['Nový student', null, 'Nový žák'],
    ['Žákovská knížka', 'Secondary', 'Žákovská knížka'],
    ['Studentský průkaz', 'Elementary', 'Studentský průkaz'],
  ] as const)('%s (%s) → %s', (text, type, expected) => {
    expect(localizeStudentTerm(text, type)).toBe(expected);
  });
});

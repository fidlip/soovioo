/** ZŠ = "žák", SŠ = "student" — see `localizeStudentTerm`. */
export type SchoolType = 'Elementary' | 'Secondary';

/** Every declined form of the two words, pairwise. Mirror of
 * `StudentTerm.cs` on the backend — keep the two tables in sync. */
const FORMS: [zak: string, student: string][] = [
  ['žák', 'student'],
  ['žáka', 'studenta'],
  ['žákovi', 'studentovi'],
  ['žákem', 'studentem'],
  ['žáku', 'studente'],
  ['žáci', 'studenti'],
  ['žáků', 'studentů'],
  ['žákům', 'studentům'],
  ['žáky', 'studenty'],
  ['žácích', 'studentech'],
];

const TO_STUDENT = new Map(FORMS.map(([zak, student]) => [zak, student]));
const TO_ZAK = new Map(FORMS.map(([zak, student]) => [student, zak]));

// Letters spelled out (Latin incl. diacritics, U+00C0–U+024F) instead of
// `\p{L}`: the mobile app's V8 has no ICU, so a `\p{…}` regex is a syntax
// error there and the whole bundle fails to load.
const WORD =
  /(?<![A-Za-z\u00C0-\u024F])(žák[A-Za-z\u00C0-\u024F]*|žác[A-Za-z\u00C0-\u024F]*|student[A-Za-z\u00C0-\u024F]*)(?![A-Za-z\u00C0-\u024F])/gi;

/** Rewrites every form of "žák"/"student" in a Czech text to the one the
 * school uses. Both words are masculine animate, so the surrounding words
 * ("Všichni žáci" → "Všichni studenti") never need to change. Other words
 * sharing the stem ("Žákovská", "studentský") are left alone. */
export function localizeStudentTerm(
  text: string,
  schoolType: SchoolType | null,
): string {
  const table = schoolType === 'Secondary' ? TO_STUDENT : TO_ZAK;
  return text.replace(WORD, (word) => {
    const replacement = table.get(word.toLowerCase());
    if (!replacement) {
      return word;
    }
    return word[0] === word[0].toUpperCase()
      ? replacement[0].toUpperCase() + replacement.slice(1)
      : replacement;
  });
}

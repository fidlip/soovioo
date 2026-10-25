import {
  RetentionCategory,
  RetentionPolicy,
  StudentLeaveReason,
} from '@soovioo/shared';

export const LEAVE_REASON_LABELS: Record<StudentLeaveReason, string> = {
  Transfer: 'Přestup na jinou školu',
  Graduated: 'Ukončení vzdělávání',
  Expelled: 'Vyloučení',
  Other: 'Jiný',
};

export const LEAVE_REASON_OPTIONS = (
  Object.keys(LEAVE_REASON_LABELS) as StudentLeaveReason[]
).map((value) => ({ value, label: LEAVE_REASON_LABELS[value] }));

export const RETENTION_CATEGORY_LABELS: Record<RetentionCategory, string> = {
  Photos: 'Fotka',
  Contacts: 'Kontakty',
  Messages: 'Zprávy o žákovi',
  Homework: 'Domácí úkoly',
  Behavior: 'Chování',
  Attendance: 'Docházka',
  Grades: 'Známky',
  Payments: 'Platby',
  Profile: 'Celý profil',
};

/** The policy's "years" field of each category, in the order shown. */
export const RETENTION_YEAR_FIELDS: {
  category: RetentionCategory;
  field: keyof RetentionPolicy;
  hint: string;
}[] = [
  {
    category: 'Photos',
    field: 'photosYears',
    hint: 'Fotka žáka a záznam o souhlasu s ní.',
  },
  {
    category: 'Contacts',
    field: 'contactsYears',
    hint: 'Kontaktní telefon a e-mail žáka.',
  },
  {
    category: 'Messages',
    field: 'messagesYears',
    hint: 'Konverzace, které se týkají žáka, i s přílohami.',
  },
  {
    category: 'Homework',
    field: 'homeworkYears',
    hint: 'Zadání pro žáka a jeho splnění.',
  },
  {
    category: 'Behavior',
    field: 'behaviorYears',
    hint: 'Pochvaly, napomenutí a klasifikace chování.',
  },
  {
    category: 'Attendance',
    field: 'attendanceYears',
    hint: 'Absence a omluvenky.',
  },
  {
    category: 'Grades',
    field: 'gradesYears',
    hint: 'Všechny známky žáka.',
  },
  {
    category: 'Payments',
    field: 'paymentsYears',
    hint: 'Předpisy plateb a jejich úhrady.',
  },
  {
    category: 'Profile',
    field: 'profileYears',
    hint: 'Vše ostatní: záznam žáka s údaji pro matriku, jeho účet a účty rodičů, kterým ve škole nezůstane jiné dítě. Žádná kategorie nevydrží déle.',
  },
];

/** Local (not UTC) y-m-d — see admin-page's toDateOnlyString. */
export function toDateOnly(date: Date): string {
  const year = date.getFullYear();
  const month = `${date.getMonth() + 1}`.padStart(2, '0');
  const day = `${date.getDate()}`.padStart(2, '0');
  return `${year}-${month}-${day}`;
}

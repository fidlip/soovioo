import {
  RetentionCategory,
  RetentionPolicy,
  StaffLeaveNoticeGroup,
  StaffLeaveReason,
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

export const STAFF_LEAVE_REASON_OPTIONS: {
  value: StaffLeaveReason;
  label: string;
}[] = [
  { value: 'Agreement', label: 'Dohoda' },
  { value: 'Notice', label: 'Výpověď' },
  { value: 'FixedTermEnd', label: 'Konec smlouvy na dobu určitou' },
  { value: 'Retirement', label: 'Odchod do důchodu' },
  { value: 'Other', label: 'Jiný' },
];

export const STAFF_LEAVE_NOTICE_LABELS: Record<StaffLeaveNoticeGroup, string> =
  {
    LedClasses: 'Žáci a rodiče tříd, kde je třídní',
    TaughtClasses: 'Žáci a rodiče tříd, které učí',
    Colleagues: 'Kolegové',
  };

export const DAY_LABELS: Record<string, string> = {
  Monday: 'Po',
  Tuesday: 'Út',
  Wednesday: 'St',
  Thursday: 'Čt',
  Friday: 'Pá',
  Saturday: 'So',
  Sunday: 'Ne',
};

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

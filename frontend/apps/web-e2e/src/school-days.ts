import { PlaywrightWorkerArgs } from '@playwright/test';

interface SchoolYearDays {
  teachingStart: string;
  teachingEnd: string;
  daysOff?: { from: string; to: string; classIds: number[] }[];
}

const apiUrl = () => {
  const url = new URL(process.env['BASE_URL'] || 'http://localhost:4200');
  return `http://${url.hostname}:${Number(url.port || 4200) + 880}/api`;
};

const iso = (d: Date) =>
  `${d.getFullYear()}-${`${d.getMonth() + 1}`.padStart(2, '0')}-${`${d.getDate()}`.padStart(2, '0')}`;

/** "07.10.2026", as typed in the date picker. */
export const typed = (d: Date) =>
  `${`${d.getDate()}`.padStart(2, '0')}.${`${d.getMonth() + 1}`.padStart(2, '0')}.${d.getFullYear()}`;

/** The seeded school's days of lessons — a school year's weekdays from its
 * first to its last day of lessons, but its days off. Records can only be
 * written in a school year. */
export async function teachingDays({
  playwright,
}: Pick<PlaywrightWorkerArgs, 'playwright'>): Promise<Date[]> {
  const request = await playwright.request.newContext();
  try {
    const api = apiUrl();
    const { token } = await (
      await request.post(`${api}/auth/login`, {
        data: { username: 'admin', password: 'admin123', schoolSlug: 'skola' },
      })
    ).json();
    const years: SchoolYearDays[] = await (
      await request.get(`${api}/school-years`, {
        headers: { Authorization: `Bearer ${token}` },
      })
    ).json();
    const days: Date[] = [];
    for (const year of years) {
      const [y, m, d] = year.teachingStart.split('-').map(Number);
      for (
        let day = new Date(y, m - 1, d);
        iso(day) <= year.teachingEnd;
        day = new Date(day.getFullYear(), day.getMonth(), day.getDate() + 1)
      ) {
        const date = iso(day);
        const off = (year.daysOff ?? []).some(
          (o) => o.classIds.length === 0 && o.from <= date && date <= o.to,
        );
        if (day.getDay() !== 0 && day.getDay() !== 6 && !off) {
          days.push(day);
        }
      }
    }
    return days;
  } finally {
    await request.dispose();
  }
}

/** Days before today (`past`) or after it. */
export function relativeToToday(days: Date[], past: boolean): Date[] {
  const today = iso(new Date());
  return days.filter((d) => (past ? iso(d) < today : iso(d) > today));
}

export function pick<T>(items: T[]): T | undefined {
  return items[Math.floor(Math.random() * items.length)];
}

/** Mondays whose Tuesday is a day of lessons too, with that Tuesday. */
export function mondaysAndTuesdays(days: Date[]): [Date, Date][] {
  const set = new Set(days.map(iso));
  return days
    .filter((d) => d.getDay() === 1)
    .map((d): [Date, Date] => [
      d,
      new Date(d.getFullYear(), d.getMonth(), d.getDate() + 1),
    ])
    .filter(([, tuesday]) => set.has(iso(tuesday)));
}

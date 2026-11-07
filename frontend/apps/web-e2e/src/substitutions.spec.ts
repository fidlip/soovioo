import { expect, test } from './fixtures';
import { VersionsApi, versionsApi } from './timetable-versions';

function iso(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

/** A day the teacher teaches, far enough ahead that nobody else's timetable
 * changes, before the versions other tests publish (900 days ahead and later). */
async function teachersDay(
  admin: VersionsApi,
  teacherId: number,
): Promise<{ date: string; period: number; who: string }> {
  const date = new Date();
  date.setDate(date.getDate() + 300 + Math.floor(Math.random() * 500));
  for (let i = 0; i < 14; i++, date.setDate(date.getDate() + 1)) {
    const day = await (
      await admin.request.get(`${admin.api}/substitutions/days/${iso(date)}`, {
        headers: admin.headers,
      })
    ).json();
    const lesson = day.lessons?.find(
      (l: { teacherId: number; studentGroupId: number | null }) =>
        l.teacherId === teacherId && l.studentGroupId === null,
    );
    if (lesson && day.absences.length === 0 && day.publishedAt === null) {
      return { date: iso(date), period: lesson.periodNumber, who: lesson.who };
    }
  }
  throw new Error('The teacher teaches on no day.');
}

async function teacherId(admin: VersionsApi): Promise<number> {
  const teachers: { id: number; username: string }[] = await (
    await admin.request.get(`${admin.api}/teachers`, { headers: admin.headers })
  ).json();
  return teachers.find((t) => t.username === 'ucitel')!.id;
}

/** Back as the timetable has it, published, so nobody sees the day changed. */
async function cleanUp(admin: VersionsApi, date: string): Promise<void> {
  const day = await (
    await admin.request.get(`${admin.api}/substitutions/days/${date}`, {
      headers: admin.headers,
    })
  ).json();
  for (const lesson of day.lessons) {
    if (lesson.changeId !== null) {
      await admin.request.delete(
        `${admin.api}/substitutions/changes/${lesson.changeId}`,
        { headers: admin.headers },
      );
    }
  }
  for (const absence of day.absences) {
    await admin.request.delete(
      `${admin.api}/substitutions/absences/${absence.id}`,
      { headers: admin.headers },
    );
  }
  await admin.request.post(`${admin.api}/substitutions/days/${date}/publish`, {
    headers: admin.headers,
  });
}

test("an absent teacher's lesson is cancelled in Suplování and once the day is published the teacher sees it and hears of it", async ({
  page,
  request,
}) => {
  test.slow();
  await page.goto('/skola/login');
  await page.locator('#username').fill('admin');
  await page.locator('#password').fill('admin123');
  await page.getByRole('button', { name: 'Přihlásit se' }).click();
  await expect(page).toHaveURL(/\/skola\/home$/);
  const admin = await versionsApi(page, request);
  const teacher = await teacherId(admin);
  const { date, period, who } = await teachersDay(admin, teacher);
  try {
    await page.goto(
      `/skola/admin?group=teaching&section=substitutions&day=${date}`,
    );
    const [, month, day] = date.split('-');
    await expect(page.getByTestId('substitution-status')).toHaveText(
      'Nezveřejněno',
    );

    await page.getByRole('button', { name: 'Nepřítomnost učitele' }).click();
    const absence = page.getByRole('dialog', { name: 'Nepřítomnost učitele' });
    await absence.getByRole('combobox', { name: 'Učitel' }).click();
    await page.getByRole('option', { name: 'Jana Učitelová' }).click();
    await absence.getByRole('button', { name: 'Uložit' }).click();
    await expect(page.getByTestId('teacher-absence')).toContainText(
      'Jana Učitelová',
    );
    const lesson = page
      .locator(
        `[data-testid="substitution-lesson"][aria-label^="${period}. hodina ${who} "]`,
      )
      .first();
    await expect(lesson).toContainText('Chybí zástup');

    await lesson.click();
    const edit = page.getByRole('dialog');
    await edit.getByRole('combobox', { name: 'Co se mění' }).click();
    await page.getByRole('option', { name: 'Odpadá' }).click();
    await edit.getByRole('button', { name: 'Uložit' }).click();
    await expect(lesson).toContainText('Odpadá');
    await page.getByLabel('Vyřešené hodiny').uncheck();
    await expect(lesson).toBeHidden();
    await page.getByLabel('Všechny hodiny dne').check();
    await expect(lesson).toContainText('Odpadá');

    await page.getByRole('button', { name: 'Zveřejnit den' }).click();
    await page
      .getByRole('alertdialog')
      .getByRole('button', { name: 'Zveřejnit' })
      .click();
    await expect(page.getByTestId('substitution-status')).toContainText(
      'Zveřejněno',
    );

    await page.context().clearCookies();
    await page.goto('/skola/login');
    await page.locator('#username').fill('ucitel');
    await page.locator('#password').fill('ucitel123');
    await page.getByRole('button', { name: 'Přihlásit se' }).click();
    await expect(page).toHaveURL(/\/skola\/home$/);
    const weekday = ['ne', 'po', 'út', 'st', 'čt', 'pá', 'so'][
      new Date(`${date}T00:00:00`).getDay()
    ];
    await page
      .getByRole('button', { name: /^Upozornění, \d+ nepřečtených$/ })
      .click();
    await page
      .getByRole('region', { name: 'Upozornění' })
      .getByRole('button', {
        name: `Změny v rozvrhu na ${weekday} ${Number(day)}. ${Number(month)}.`,
        exact: false,
      })
      .first()
      .click();

    await expect(page).toHaveURL(/\/skola\/timetable$/);
    await expect(
      page
        .locator(`td[data-period="${period}"]`)
        .getByTestId('lesson-change')
        .filter({ hasText: 'Odpadá' }),
    ).toBeVisible();
  } finally {
    await cleanUp(admin, date);
  }
});

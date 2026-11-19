import { expect, test } from './fixtures';
import { pick, relativeToToday, teachingDays } from './school-days';
import { versionsApi } from './timetable-versions';

const iso = (d: Date) =>
  `${d.getFullYear()}-${`${d.getMonth() + 1}`.padStart(2, '0')}-${`${d.getDate()}`.padStart(2, '0')}`;

interface Lesson {
  periodNumber: number;
  subjectName: string;
  who: string;
  topic: string | null;
  canWrite: boolean;
}

test('the management writes the topic of a past lesson into the třídní kniha and takes it back', async ({
  page,
  request,
  playwright,
}) => {
  await page.goto('/skola/login');
  await page.locator('#username').fill('admin');
  await page.locator('#password').fill('admin123');
  await page.getByRole('button', { name: 'Přihlásit se' }).click();
  await expect(page).toHaveURL(/\/skola\/home$/);
  const admin = await versionsApi(page, request);
  const classes: { id: number; name: string }[] = await (
    await request.get(`${admin.api}/class-book/classes`, {
      headers: admin.headers,
    })
  ).json();

  // A past day of lessons with an unwritten lesson in some class.
  const days = relativeToToday(await teachingDays({ playwright }), true);
  let found: { date: string; classId: number; lesson: Lesson } | undefined;
  for (let tries = 0; tries < 10 && !found; tries++) {
    const date = iso(pick(days)!);
    const schoolClass = pick(classes)!;
    const day = await (
      await request.get(
        `${admin.api}/class-book/day?date=${date}&classId=${schoolClass.id}`,
        { headers: admin.headers },
      )
    ).json();
    const lesson = (day.lessons as Lesson[]).find(
      (l) => l.canWrite && l.topic === null,
    );
    if (lesson) {
      found = { date, classId: schoolClass.id, lesson };
    }
  }
  expect(found, 'a past lesson to write').toBeDefined();
  const { date, classId, lesson } = found!;
  const heading = `${lesson.periodNumber}. hodina · ${lesson.subjectName} · ${lesson.who}`;
  const topic = `Téma ${Date.now()}`;

  await page.goto(`/skola/class-book?day=${date}&class=${classId}`);
  await page.getByRole('button', { name: `Zapsat ${heading}` }).click();
  const dialog = page.getByRole('dialog', { name: heading });
  await dialog.getByLabel('Téma hodiny').fill(topic);
  await dialog.getByRole('button', { name: 'Uložit' }).click();
  await expect(dialog).toBeHidden();
  const row = page.getByTestId('class-book-lesson').filter({ hasText: topic });
  await expect(row).toContainText('Zapsal(a)');

  await page.reload();
  await expect(row).toBeVisible();
  await page.getByRole('button', { name: `Upravit ${heading}` }).click();
  await dialog.getByRole('button', { name: 'Smazat zápis' }).click();
  await expect(dialog).toBeHidden();
  await expect(page.getByText(topic)).toHaveCount(0);
});

test("a teacher's třídní kniha opens at the lessons they teach", async ({
  page,
}) => {
  await page.goto('/skola/login');
  await page.locator('#username').fill('ucitel');
  await page.locator('#password').fill('ucitel123');
  await page.getByRole('button', { name: 'Přihlásit se' }).click();
  await expect(page).toHaveURL(/\/skola\/home$/);

  await page.getByRole('link', { name: 'Třídní kniha' }).first().click();
  await expect(page).toHaveURL(/\/skola\/class-book$/);
  await expect(page.getByTestId('class-book-class')).toContainText(
    'Moje hodiny',
  );
});

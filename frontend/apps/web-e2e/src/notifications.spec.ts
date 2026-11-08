import { APIRequestContext, Page } from '@playwright/test';
import { expect, test } from './fixtures';

/** The dev API runs 880 ports above the web dev server (see devApiPort). */
function apiUrl(page: Page): string {
  const url = new URL(page.url());
  return `http://${url.hostname}:${Number(url.port || 4200) + 880}/api`;
}

/** The teacher writes to Jan Novák's family — straight through the API. */
async function writeToFamily(
  page: Page,
  request: APIRequestContext,
  subject: string,
) {
  const api = apiUrl(page);
  const { token } = await (
    await request.post(`${api}/auth/login`, {
      data: { username: 'ucitel', password: 'ucitel123', schoolSlug: 'skola' },
    })
  ).json();
  const headers = { Authorization: `Bearer ${token}` };
  const students = await (
    await request.get(`${api}/students`, { headers })
  ).json();
  const studentId = students.find(
    (s: { firstName: string; lastName: string }) =>
      s.firstName === 'Jan' && s.lastName === 'Novák',
  ).id;
  expect(
    (
      await request.post(`${api}/conversations`, {
        headers,
        data: { subject, body: 'Zítra s sebou přezůvky.', studentId },
      })
    ).ok(),
  ).toBe(true);
}

test('a message to the family shows under the parent’s bell and opens the conversation', async ({
  page,
  request,
}) => {
  const subject = `E2E upozornění ${Date.now()}`;
  await page.goto('/skola/login');
  await writeToFamily(page, request, subject);

  await page.locator('#username').fill('rodic');
  await page.locator('#password').fill('rodic123');
  await page.getByRole('button', { name: 'Přihlásit se' }).click();
  await expect(page).toHaveURL(/\/skola\/home$/);

  await page
    .getByRole('button', { name: /^Upozornění, \d+ nepřečtených$/ })
    .click();
  const bell = page.getByRole('region', { name: 'Upozornění' });
  const item = bell.getByRole('button', { name: `Nová zpráva: ${subject}` });
  await expect(item).toBeVisible();
  await item.click();

  await expect(page).toHaveURL(/\/skola\/messages\?conversation=\d+$/);
  await expect(
    page.getByText('Zítra s sebou přezůvky.', { exact: true }),
  ).toBeVisible();
});

/** A new grade for Jan Novák (the parent `rodic`'s child) in the school's
 * first subject, or another one so that tests running alongside don't take
 * its notification for theirs. */
async function addGrade(page: Page, request: APIRequestContext, subject = 0) {
  const api = apiUrl(page);
  const { token } = await (
    await request.post(`${api}/auth/login`, {
      data: { username: 'admin', password: 'admin123', schoolSlug: 'skola' },
    })
  ).json();
  const headers = { Authorization: `Bearer ${token}` };
  const students = await (
    await request.get(`${api}/students`, { headers })
  ).json();
  const subjects = await (
    await request.get(`${api}/subjects`, { headers })
  ).json();
  const studentId = students.find(
    (s: { firstName: string; lastName: string }) =>
      s.firstName === 'Jan' && s.lastName === 'Novák',
  ).id;
  const response = await request.post(`${api}/grades`, {
    headers,
    data: {
      studentId,
      subjectId: subjects[subject].id,
      value: 2,
      weight: 1,
      note: `E2E ${Date.now()}`,
    },
  });
  expect(response.ok()).toBe(true);
  return (await response.json()) as { id: number; subjectName: string };
}

async function logInAsParent(page: Page) {
  await page.locator('#username').fill('rodic');
  await page.locator('#password').fill('rodic123');
  await page.getByRole('button', { name: 'Přihlásit se' }).click();
}

test('a grade’s notification opens the grade itself and marks it', async ({
  page,
  request,
}) => {
  await page.goto('/skola/login');
  const { id: gradeId, subjectName } = await addGrade(page, request);
  await logInAsParent(page);
  await expect(page).toHaveURL(/\/skola\/home$/);

  await page
    .getByRole('button', { name: /^Upozornění, \d+ nepřečtených$/ })
    .click();
  await page
    .getByRole('region', { name: 'Upozornění' })
    .getByRole('button', { name: `Nová známka — ${subjectName}` })
    .first()
    .click();

  await expect(page).toHaveURL(/\/skola\/grades$/);
  await expect(page.locator(`#grade-${gradeId}`)).toHaveClass(/app-highlight/);
});

/** The parent's notifications about this grade. */
async function parentsAbout(
  page: Page,
  request: APIRequestContext,
  gradeId: number,
) {
  const api = apiUrl(page);
  const { token } = await (
    await request.post(`${api}/auth/login`, {
      data: { username: 'rodic', password: 'rodic123', schoolSlug: 'skola' },
    })
  ).json();
  const { items } = await (
    await request.get(`${api}/notifications`, {
      headers: { Authorization: `Bearer ${token}` },
    })
  ).json();
  return (
    items as { type: string; targetId: number; title: string; read: boolean }[]
  ).filter((n) => n.type === 'Grade' && n.targetId === gradeId);
}

async function adminHeaders(page: Page, request: APIRequestContext) {
  const { token } = await (
    await request.post(`${apiUrl(page)}/auth/login`, {
      data: { username: 'admin', password: 'admin123', schoolSlug: 'skola' },
    })
  ).json();
  return { Authorization: `Bearer ${token}` };
}

test.describe('a grade that changed or went', () => {
  // Asking for the deleted grade answers 404.
  test.use({ allowedConsoleMessages: [/status of 404/] });

  test('its notification tells the change, then goes with it, and its link says so', async ({
    page,
    request,
  }) => {
    await page.goto('/skola/login');
    const grade = await addGrade(page, request, 1);
    const gradeId = grade.id;
    const api = apiUrl(page);
    const headers = await adminHeaders(page, request);

    expect(
      (
        await request.put(`${api}/grades/${gradeId}`, {
          headers,
          data: { ...grade, value: 3 },
        })
      ).ok(),
    ).toBe(true);
    const changed = await parentsAbout(page, request, gradeId);
    expect(changed.map((n) => n.title)).toEqual([
      `Změna známky — ${grade.subjectName}`,
    ]);

    expect(
      (await request.delete(`${api}/grades/${gradeId}`, { headers })).ok(),
    ).toBe(true);
    expect(await parentsAbout(page, request, gradeId)).toEqual([]);

    await logInAsParent(page);
    await expect(page).toHaveURL(/\/skola\/home$/);
    await page.goto(`/skola/grades?grade=${gradeId}`);
    await expect(
      page.getByText('Tato položka už neexistuje nebo ji nevidíte.'),
    ).toBeVisible();
  });
});

test('a link from the daily e-mail leads back to the thing after logging in', async ({
  page,
  request,
}) => {
  await page.goto('/skola/login');
  const { id: gradeId } = await addGrade(page, request);

  await page.goto(`/skola/grades?grade=${gradeId}`);
  await expect(page).toHaveURL(/\/skola\/login\?returnUrl=/);
  await logInAsParent(page);

  await expect(page).toHaveURL(/\/skola\/grades$/);
  await expect(page.locator(`#grade-${gradeId}`)).toHaveClass(/app-highlight/);
});

test('the bell can show only the unread notifications', async ({
  page,
  request,
}) => {
  const subject = `E2E nepřečtené ${Date.now()}`;
  await page.goto('/skola/login');
  await writeToFamily(page, request, subject);
  await writeToFamily(page, request, `B ${subject}`);
  await logInAsParent(page);
  await expect(page).toHaveURL(/\/skola\/home$/);

  const bellButton = page.getByRole('button', {
    name: /^Upozornění, \d+ nepřečtených$/,
  });
  const bell = page.getByRole('region', { name: 'Upozornění' });
  const item = bell.getByRole('button', { name: `Nová zpráva: ${subject}` });
  const unreadOnly = bell.getByLabel('Pouze nepřečtené');
  await bellButton.click();
  await unreadOnly.check();
  await item.click();
  await expect(page).toHaveURL(/\/skola\/messages\?conversation=\d+$/);

  // Remembered on this device: the read one is gone next time.
  await page.reload();
  await page.getByRole('button', { name: /^Upozornění/ }).click();
  await expect(unreadOnly).toBeChecked();
  await expect(
    bell.getByRole('button', { name: `Nová zpráva: B ${subject}` }),
  ).toBeVisible();
  await expect(item).toHaveCount(0);
  await unreadOnly.uncheck();
  await expect(item).toBeVisible();
});

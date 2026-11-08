import { APIRequestContext, Page } from '@playwright/test';
import { expect, test } from './fixtures';

/** The dev API runs 880 ports above the web dev server (see devApiPort). */
function apiUrl(page: Page): string {
  const url = new URL(page.url());
  return `http://${url.hostname}:${Number(url.port || 4200) + 880}/api`;
}

test('a message to the family shows under the parent’s bell and opens the conversation', async ({
  page,
  request,
}) => {
  const subject = `E2E upozornění ${Date.now()}`;
  await page.goto('/skola/login');

  // The teacher writes to Jan Novák's family — straight through the API.
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

/** A new grade for Jan Novák (the parent `rodic`'s child), its id. */
async function addGrade(page: Page, request: APIRequestContext) {
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
      subjectId: subjects[0].id,
      value: 2,
      weight: 1,
      note: `E2E ${Date.now()}`,
    },
  });
  expect(response.ok()).toBe(true);
  return ((await response.json()) as { id: number }).id;
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
  const gradeId = await addGrade(page, request);
  await logInAsParent(page);
  await expect(page).toHaveURL(/\/skola\/home$/);

  await page
    .getByRole('button', { name: /^Upozornění, \d+ nepřečtených$/ })
    .click();
  await page
    .getByRole('region', { name: 'Upozornění' })
    .getByRole('button', { name: /^Nová známka/ })
    .first()
    .click();

  await expect(page).toHaveURL(/\/skola\/grades$/);
  await expect(page.locator(`#grade-${gradeId}`)).toHaveClass(/app-highlight/);
});

test('a link from the daily e-mail leads back to the thing after logging in', async ({
  page,
  request,
}) => {
  await page.goto('/skola/login');
  const gradeId = await addGrade(page, request);

  await page.goto(`/skola/grades?grade=${gradeId}`);
  await expect(page).toHaveURL(/\/skola\/login\?returnUrl=/);
  await logInAsParent(page);

  await expect(page).toHaveURL(/\/skola\/grades$/);
  await expect(page.locator(`#grade-${gradeId}`)).toHaveClass(/app-highlight/);
});

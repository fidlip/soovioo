import { Page } from '@playwright/test';
import { expect, test } from './fixtures';

/** dd.mm.yyyy `days` from today — the DatePicker's input format. */
function inDays(days: number): string {
  const date = new Date();
  date.setDate(date.getDate() + days);
  const pad = (n: number) => `${n}`.padStart(2, '0');
  return `${pad(date.getDate())}.${pad(date.getMonth() + 1)}.${date.getFullYear()}`;
}

async function login(page: Page, username: string) {
  await page.goto('/skola/login');
  await page.locator('#username').fill(username);
  await page.locator('#password').fill(`${username}123`);
  await page.getByRole('button', { name: 'Přihlásit se' }).click();
  await expect(page).toHaveURL(/\/skola\/home$/);
}

async function openStudents(page: Page) {
  await login(page, 'admin');
  await page.getByRole('link', { name: 'Administrace', exact: true }).click();
  await page.getByRole('tab', { name: 'Osoby' }).click();
}

/** Adds a pupil of the test's own through Administrace → Osoby — the seeded
 * one is used by other tests. Returns the table row. */
async function addPupil(page: Page, lastName: string) {
  await page.getByRole('button', { name: 'Nová osoba' }).click();
  const dialog = page.getByRole('dialog', { name: 'Nová osoba' });
  await dialog.getByRole('textbox', { name: /^Jméno/ }).fill('Ota');
  await dialog.getByRole('textbox', { name: /^Příjmení/ }).fill(lastName);
  await dialog.locator('p-multiselect[name="profiles"]').click();
  const list = page.getByRole('listbox');
  await list.getByRole('option', { name: 'Žák', exact: true }).click();
  // Escape only once the list is open — otherwise it closes the dialog.
  await page.keyboard.press('Escape');
  await expect(list).toBeHidden();
  await dialog.getByRole('button', { name: 'Vytvořit osobu' }).click();
  await expect(dialog).toBeHidden();
  return page
    .getByRole('tabpanel', { name: 'Osoby' })
    .locator('tr', { hasText: lastName });
}

/** The dev API runs 880 ports above the web dev server (see devApiPort). */
function apiUrl(page: Page): string {
  const url = new URL(page.url());
  return `http://${url.hostname}:${Number(url.port || 4200) + 880}/api`;
}

test.describe('deleting a pupil', () => {
  // The refused delete is a 409, which the browser logs.
  test.use({ allowedConsoleMessages: [/409/] });

  test('a pupil with history is not deleted, the admin is offered Ukončení vzdělávání', async ({
    page,
    request,
  }) => {
    const lastName = `Znamka${Date.now()}`;
    await openStudents(page);
    const row = await addPupil(page, lastName);

    // Their first grade — straight through the API.
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
      (s: { lastName: string }) => s.lastName === lastName,
    ).id;
    expect(
      (
        await request.post(`${api}/grades`, {
          headers,
          data: {
            studentId,
            subjectId: subjects[0].id,
            value: 1,
            weight: 1,
            note: null,
          },
        })
      ).ok(),
    ).toBe(true);

    await row.getByRole('button', { name: 'Smazat' }).click();
    await page
      .getByRole('alertdialog')
      .getByRole('button', { name: 'Smazat' })
      .click();

    const refusal = page.getByRole('alertdialog', {
      name: 'Žáka nejde smazat',
    });
    await expect(refusal).toContainText('už má 1 známku');
    await refusal.getByRole('button', { name: 'Ukončit vzdělávání' }).click();
    // Closing, it gives the focus back — typing before that loses keys.
    await expect(refusal).toBeHidden();

    const leaveDialog = page.getByRole('dialog', {
      name: `Ukončení vzdělávání — Ota ${lastName}`,
    });
    await leaveDialog
      .getByLabel('Poslední den ve škole')
      .pressSequentially(inDays(-1));
    await leaveDialog.getByRole('button', { name: 'Uložit' }).click();
    await expect(row).toHaveCount(0);

    await page.getByRole('tab', { name: 'Archiv a výmaz údajů' }).click();
    const formerRow = page
      .getByRole('tabpanel', { name: 'Archiv a výmaz údajů' })
      .locator('tr', { hasText: lastName });
    await formerRow.getByRole('button', { name: 'Vymazat vše hned' }).click();
    await page
      .getByRole('alertdialog')
      .getByRole('button', { name: 'Vymazat' })
      .click();
    await expect(formerRow).toHaveCount(0);
  });
});

test('a former pupil moves to the archive and can be erased', async ({
  page,
  browser,
}) => {
  // A pupil of its own — the seeded one is used by other tests.
  const lastName = `Archiv${Date.now()}`;

  await openStudents(page);
  const row = await addPupil(page, lastName);
  await row.getByRole('button', { name: 'Ukončení vzdělávání' }).click();
  const leaveDialog = page.getByRole('dialog', {
    name: `Ukončení vzdělávání — Ota ${lastName}`,
  });
  const lastDay = leaveDialog.getByLabel('Poslední den ve škole');
  // Typed key by key — the DatePicker ignores a pasted value.
  await lastDay.pressSequentially(inDays(30));
  await leaveDialog.getByLabel('Poznámka').fill('Přestup');
  await leaveDialog.getByRole('button', { name: 'Uložit' }).click();
  await expect(leaveDialog).toBeHidden();
  await expect(row.getByText(/Končí k/)).toBeVisible();

  // The class teacher of 5.A was told (the default).
  const teacher = await (await browser.newContext()).newPage();
  await login(teacher, 'ucitel');
  await teacher.getByRole('link', { name: 'Zprávy', exact: true }).click();
  await expect(
    teacher.getByText(`Ukončení vzdělávání — Ota ${lastName}`),
  ).toBeVisible();
  await teacher.context().close();

  // Opened again, the dialog shows what was saved.
  await row.getByRole('button', { name: 'Ukončení vzdělávání' }).click();
  await expect(leaveDialog.getByLabel('Poznámka')).toHaveValue('Přestup');
  await lastDay.press('Control+a');
  await lastDay.pressSequentially(inDays(-1));
  await leaveDialog.getByRole('button', { name: 'Uložit' }).click();
  await expect(leaveDialog).toBeHidden();
  await expect(row).toHaveCount(0);

  await page.getByRole('tab', { name: 'Archiv a výmaz údajů' }).click();
  const formerRow = page
    .getByRole('tabpanel', { name: 'Archiv a výmaz údajů' })
    .locator('tr', { hasText: lastName });
  await expect(formerRow.getByText('Bývalý')).toBeVisible();

  await formerRow.getByRole('button', { name: 'Vymazat vše hned' }).click();
  await page
    .getByRole('alertdialog')
    .getByRole('button', { name: 'Vymazat' })
    .click();
  await expect(formerRow).toHaveCount(0);
});

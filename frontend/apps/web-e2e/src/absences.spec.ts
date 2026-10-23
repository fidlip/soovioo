import { expect, test } from './fixtures';

async function login(
  page: import('@playwright/test').Page,
  username: string,
  password: string,
) {
  await page.goto('/skola/login');
  await page.locator('#username').fill(username);
  await page.locator('#password').fill(password);
  await page.getByRole('button', { name: 'Přihlásit se' }).click();
  await expect(page).toHaveURL(/\/skola\/home$/);
  await page.getByRole('link', { name: 'Docházka', exact: true }).click();
  await expect(page).toHaveURL(/\/skola\/absences$/);
}

/** A random past date, so this run's absence is the only row with it — even
 * against a local database where earlier runs (or other spec files running
 * in parallel) left absences behind. */
function randomPastDate(): { iso: string; typed: string } {
  const year = 2001 + Math.floor(Math.random() * 20);
  const month = 1 + Math.floor(Math.random() * 12);
  const day = 1 + Math.floor(Math.random() * 28);
  const pad = (n: number) => `${n}`.padStart(2, '0');
  return {
    iso: `${year}-${pad(month)}-${pad(day)}`,
    typed: `${pad(day)}.${pad(month)}.${year}`,
  };
}

// One absence goes through its whole life: the teacher records it, the
// parent excuses it, the teacher approves it — each step depends on the
// previous one.
test.describe.serial('absence excuse workflow', () => {
  const date = randomPastDate();
  const row = (page: import('@playwright/test').Page) =>
    page.locator('table tbody tr').filter({ hasText: date.iso });

  test('teacher records an absence', async ({ page }) => {
    await login(page, 'ucitel', 'ucitel123');

    await page.getByRole('button', { name: 'Zapsat absenci' }).click();
    const dialog = page.getByRole('dialog', { name: 'Zapsat absenci' });
    // Focused by the dialog once its open animation ends — wait for it, so
    // it can't move focus away while the date is typed below.
    await expect(dialog.locator('#absenceStudent')).toBeFocused();
    // The dialog preselects no student; Jan Novák is 5.A's demo student.
    await dialog.locator('#absenceStudent').click();
    await page.getByRole('option', { name: 'Jan Novák' }).click();
    // Typed key by key, not fill(): the date picker parses only input that
    // follows a keydown, a filled-in value reverts on blur.
    const dateInput = dialog.locator('#absenceDate');
    await dateInput.selectText();
    await dateInput.pressSequentially(date.typed);
    await dateInput.press('Escape');
    // The only `type="submit"` button on the page (see grades.spec.ts on
    // why not by its accessible name).
    await dialog.locator('button[type="submit"]').click();

    await expect(row(page)).toHaveCount(1);
    await expect(row(page)).toContainText('Jan Novák');
    await expect(row(page)).toContainText('Neomluveno');
  });

  test('parent sees it, cannot edit it, and submits an excuse', async ({
    page,
  }) => {
    await login(page, 'rodic', 'rodic123');

    await expect(
      page.getByRole('button', { name: 'Zapsat absenci' }),
    ).toHaveCount(0);
    await expect(row(page)).toHaveCount(1);
    await expect(
      row(page).getByRole('button', { name: 'Upravit' }),
    ).toHaveCount(0);
    await expect(row(page).getByRole('button', { name: 'Smazat' })).toHaveCount(
      0,
    );

    await row(page).getByPlaceholder('Důvod omluvenky').fill('Návštěva lékaře');
    await row(page).getByRole('button', { name: 'Omluvit' }).click();

    await expect(row(page)).toContainText('Čeká na schválení');
    await expect(row(page)).toContainText('Návštěva lékaře');
  });

  test('student sees it but cannot approve it', async ({ page }) => {
    await login(page, 'zak', 'zak123');

    await expect(row(page)).toContainText('Čeká na schválení');
    await expect(
      row(page).getByRole('button', { name: 'Schválit' }),
    ).toHaveCount(0);
  });

  test('teacher approves the excuse and deletes the record', async ({
    page,
  }) => {
    await login(page, 'ucitel', 'ucitel123');

    await row(page).getByRole('button', { name: 'Schválit' }).click();
    await expect(row(page)).toContainText('Omluveno');

    // Clean up — the random date keeps reruns apart, but there's no need
    // to leave the record in a developer's local database.
    await row(page).getByRole('button', { name: 'Smazat' }).click();
    await expect(row(page)).toHaveCount(0);
  });
});

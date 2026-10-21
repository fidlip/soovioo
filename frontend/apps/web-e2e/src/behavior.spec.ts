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
  // The Home dashboard has its own "Celé chování" link too.
  await page
    .getByRole('navigation', { name: 'Hlavní navigace' })
    .getByRole('link', { name: 'Chování', exact: true })
    .click();
  await expect(page).toHaveURL(/\/skola\/behavior$/);
}

// One record goes through its life: the teacher writes it, the parent
// reads it, the teacher removes it.
test.describe.serial('behavior record', () => {
  // Unique per run, so the record is found even when earlier runs left
  // records behind in a local database.
  const text = `E2E pochvala ${Date.now()}: pomoc spolužákům`;
  const record = (page: import('@playwright/test').Page) =>
    page
      .locator('p-card', { hasText: 'Záznamy chování' })
      .locator('div.rounded-border')
      .filter({ hasText: text });

  test('teacher records a commendation', async ({ page }) => {
    await login(page, 'ucitel', 'ucitel123');

    await page.getByRole('button', { name: 'Nový záznam' }).click();
    const dialog = page.getByRole('dialog', { name: 'Nový záznam chování' });
    await dialog.locator('#recordStudent').click();
    await page.getByRole('option', { name: 'Jan Novák' }).click();
    await dialog.locator('#recordType').click();
    await page.getByRole('option', { name: 'Pochvala', exact: true }).click();
    await dialog.getByPlaceholder('Text').fill(text);
    await dialog.locator('button[type="submit"]').click();
    await expect(dialog).toBeHidden();

    await expect(record(page)).toHaveCount(1);
    await expect(record(page)).toContainText('Jan Novák');
    await expect(record(page)).toContainText('Pochvala');
  });

  test('parent reads it and cannot delete it', async ({ page }) => {
    await login(page, 'rodic', 'rodic123');

    await expect(page.getByRole('button', { name: 'Nový záznam' })).toHaveCount(
      0,
    );
    await expect(
      record(page).getByRole('button', { name: 'Smazat' }),
    ).toHaveCount(0);
    await expect(record(page)).toContainText('Nové');

    await record(page)
      .getByRole('button', { name: 'Označit jako přečtené' })
      .click();
    await expect(
      record(page).getByRole('button', { name: 'Označit jako přečtené' }),
    ).toHaveCount(0);
    await expect(record(page)).not.toContainText('Nové');
  });

  test('teacher deletes the record', async ({ page }) => {
    await login(page, 'ucitel', 'ucitel123');

    await record(page).getByRole('button', { name: 'Smazat' }).click();
    await expect(record(page)).toHaveCount(0);
  });
});

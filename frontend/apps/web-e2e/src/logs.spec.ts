import { expect, test } from './fixtures';

async function login(
  page: import('@playwright/test').Page,
  username: string,
  password: string,
) {
  await page.goto('/demo/login');
  await page.locator('#username').fill(username);
  await page.locator('#password').fill(password);
  await page.getByRole('button', { name: 'Přihlásit se' }).click();
  await expect(page).toHaveURL(/\/demo\/home$/);
}

test('school admin sees their own login in Logy, password masked', async ({
  page,
}) => {
  await login(page, 'admin', 'admin123');
  await page.getByRole('link', { name: 'Logy', exact: true }).click();
  await expect(page).toHaveURL(/\/demo\/logs$/);

  // The login just made (other tests log in in parallel, so not
  // necessarily the very first row).
  const row = page
    .locator('tbody tr')
    .filter({ hasText: 'Alena Správcová' })
    .filter({ hasText: 'Přihlášení' })
    .first();
  await expect(row).toBeVisible();
  // SuperAdmin-only column.
  await expect(page.getByRole('columnheader', { name: 'Škola' })).toHaveCount(
    0,
  );

  await row.getByRole('button', { name: 'Zobrazit detail' }).click();
  await expect(page.getByText('"password": "***"')).toBeVisible();
  await expect(page.getByText('admin123')).toHaveCount(0);

  // Clicking anywhere on the row toggles the detail too.
  await row.getByText('Alena Správcová').click();
  await expect(page.getByText('"password": "***"')).toHaveCount(0);
  await row.getByText('Alena Správcová').click();
  await expect(page.getByText('"password": "***"')).toBeVisible();
});

test('teacher has no Logy', async ({ page }) => {
  await login(page, 'ucitel', 'ucitel123');
  await expect(
    page.getByRole('link', { name: 'Logy', exact: true }),
  ).toHaveCount(0);
});

import { expect, test } from '@playwright/test';

async function login(page: import('@playwright/test').Page, username: string, password: string) {
  await page.goto('/demo/login');
  await page.locator('#username').fill(username);
  await page.locator('#password').fill(password);
  await page.getByRole('button', { name: 'Přihlásit se' }).click();
  await expect(page).toHaveURL(/\/demo\/grades$/);
}

test('teacher can record a grade and see it appear in the table', async ({ page }) => {
  await login(page, 'ucitel', 'ucitel123');

  const rowsBefore = await page.locator('table tbody tr').count();

  await page.locator('#value').fill('2');
  await page.locator('#weight').fill('4');
  await page.getByRole('button', { name: 'Zapsat' }).click();

  // Grades are listed newest-first (GradesController orders by CreatedAt
  // descending), so the row just submitted is always the first one —
  // reliable even when earlier runs left other grades with the same values.
  await expect(page.locator('table tbody tr')).toHaveCount(rowsBefore + 1);
  const newRow = page.locator('table tbody tr').first();
  await expect(newRow).toContainText('2');
  await expect(newRow).toContainText('4');
});

test('parent does not see the grade-entry form, only their child’s grades', async ({ page }) => {
  await login(page, 'rodic', 'rodic123');

  await expect(page.getByRole('button', { name: 'Zapsat' })).toHaveCount(0);
  await expect(page.locator('table tbody tr').first()).toBeVisible();
});

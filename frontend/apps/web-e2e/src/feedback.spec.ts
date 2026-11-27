import { expect, test } from './fixtures';

/**
 * Feedback goes to the backoffice (Kontakty) — what it does with it is
 * covered by the API tests. The dev instance isn't connected to one, so
 * Nastavení offers no "Zpětná vazba" there.
 */
test('feedback: no form in Nastavení without a backoffice', async ({
  page,
}) => {
  await page.goto('/skola/login');
  await page.locator('#username').fill('ucitel');
  await page.locator('#password').fill('ucitel123');
  await page.getByRole('button', { name: 'Přihlásit se' }).click();
  await expect(page).toHaveURL(/\/skola\/home$/);
  await page.goto('/skola/settings');
  await expect(page.getByRole('heading', { name: 'Nastavení' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Zpětná vazba' })).toHaveCount(
    0,
  );
});

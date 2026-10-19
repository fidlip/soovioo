import { expect, test } from './fixtures';

test('new school is created in a modal like other entities', async ({
  page,
}) => {
  await page.goto('/superadmin/login');
  await page.locator('#username').fill('superadmin');
  await page.locator('#password').fill('superadmin123');
  await page.getByRole('button', { name: 'Přihlásit se' }).click();
  await expect(page).toHaveURL(/\/superadmin$/);

  // No inline creation form on the page itself any more.
  await expect(page.locator('#schoolName')).toHaveCount(0);

  await page.getByRole('button', { name: 'Nová škola' }).click();
  const dialog = page.getByRole('dialog', { name: 'Nová škola' });
  await expect(dialog).toBeVisible();
  await expect(dialog.locator('#schoolName')).toBeVisible();
  await expect(
    dialog.getByRole('button', { name: 'Založit školu' }),
  ).toBeDisabled();
});

test('new SuperAdmin is added in a modal too', async ({ page }) => {
  await page.goto('/superadmin/login');
  await page.locator('#username').fill('superadmin');
  await page.locator('#password').fill('superadmin123');
  await page.getByRole('button', { name: 'Přihlásit se' }).click();
  await expect(page).toHaveURL(/\/superadmin$/);

  await expect(page.locator('#newSuperAdminUsername')).toHaveCount(0);

  await page.getByRole('button', { name: 'Nový superadmin' }).click();
  const dialog = page.getByRole('dialog', { name: 'Nový superadmin' });
  await expect(dialog).toBeVisible();
  await expect(dialog.locator('#newSuperAdminUsername')).toBeVisible();
  await expect(
    dialog.getByRole('button', { name: 'Přidat superadmina' }),
  ).toBeDisabled();
});

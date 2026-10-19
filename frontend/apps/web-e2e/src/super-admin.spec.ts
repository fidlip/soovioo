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

  await expect(page.locator('#superAdminUsername')).toHaveCount(0);

  await page.getByRole('button', { name: 'Nový superadmin' }).click();
  const dialog = page.getByRole('dialog', { name: 'Nový superadmin' });
  await expect(dialog).toBeVisible();
  await expect(dialog.locator('#superAdminUsername')).toBeVisible();
  await expect(
    dialog.getByRole('button', { name: 'Přidat superadmina' }),
  ).toBeDisabled();
});

test('school is edited in the same modal', async ({ page }) => {
  await page.goto('/superadmin/login');
  await page.locator('#username').fill('superadmin');
  await page.locator('#password').fill('superadmin123');
  await page.getByRole('button', { name: 'Přihlásit se' }).click();
  await expect(page).toHaveURL(/\/superadmin$/);

  await page.getByRole('button', { name: 'Upravit školu' }).first().click();
  const dialog = page.getByRole('dialog', { name: 'Upravit školu' });
  await expect(dialog).toBeVisible();
  await expect(dialog.locator('#schoolName')).not.toHaveValue('');
  // The first admin is only set when creating a school.
  await expect(dialog.locator('#adminUsername')).toHaveCount(0);
  await expect(dialog.getByRole('button', { name: 'Uložit' })).toBeEnabled();
});

test('support mode: enter a school, survive a reload, leave', async ({
  page,
}) => {
  await page.goto('/superadmin/login');
  await page.locator('#username').fill('superadmin');
  await page.locator('#password').fill('superadmin123');
  await page.getByRole('button', { name: 'Přihlásit se' }).click();
  await expect(page).toHaveURL(/\/superadmin$/);

  // A plain SuperAdmin session never shows a school's pages.
  await page.goto('/demo/grades');
  await expect(page).toHaveURL(/\/superadmin$/);

  await page.getByRole('button', { name: 'Vstoupit do školy' }).first().click();
  await expect(page).toHaveURL(/\/demo\/home$/);
  await expect(page.getByText('Režim podpory')).toBeVisible();

  await page.reload();
  await expect(page).toHaveURL(/\/demo\/home$/);
  await expect(page.getByText('Režim podpory')).toBeVisible();

  await page.getByRole('button', { name: 'Opustit' }).click();
  await expect(page).toHaveURL(/\/superadmin$/);
  await expect(page.getByText('Režim podpory')).toHaveCount(0);
});

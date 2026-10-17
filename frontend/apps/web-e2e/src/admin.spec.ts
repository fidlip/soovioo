import { expect, test } from '@playwright/test';

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

test('admin sees the full tabbed Administrace page', async ({ page }) => {
  await login(page, 'admin', 'admin123');
  await page.getByRole('link', { name: 'Administrace' }).click();

  await expect(
    page.getByRole('tab', { name: 'Třídy a předměty' }),
  ).toBeVisible();
  await expect(page.getByRole('tab', { name: 'Žáci' })).toBeVisible();
  await expect(
    page.getByRole('tab', { name: 'Uživatelé a vazby' }),
  ).toBeVisible();
  await expect(
    page.getByRole('tab', { name: 'Škola a matrika' }),
  ).toBeVisible();
});

test('class teacher sees only their own class, no tabs', async ({ page }) => {
  // Demo "ucitel" account is the class teacher (ClassTeacherId) of 5.A.
  await login(page, 'ucitel', 'ucitel123');
  await page.getByRole('link', { name: 'Administrace' }).click();

  await expect(page.getByText('Jste třídní učitel')).toBeVisible();
  await expect(page.getByRole('tab', { name: 'Třídy a předměty' })).toHaveCount(
    0,
  );
  await expect(page.getByText('Žáci')).toBeVisible();
  await expect(page.getByText('Vazby rodič ↔ žák')).toBeVisible();
  // 5.A's demo student, not any student from an unrelated class — appears
  // twice (Žáci table + Vazby rodič↔žák table), so assert on the first hit.
  await expect(page.getByText('Jan Novák').first()).toBeVisible();
});

test('parent is denied the Administrace page', async ({ page }) => {
  await login(page, 'rodic', 'rodic123');

  await expect(page.getByRole('link', { name: 'Administrace' })).toHaveCount(0);
  await page.goto('/demo/admin');
  await expect(page.getByText('Tato stránka je dostupná')).toBeVisible();
});

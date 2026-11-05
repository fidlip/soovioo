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
}

test('admin sees the full tabbed Administrace page', async ({ page }) => {
  await login(page, 'admin', 'admin123');
  await page.getByRole('link', { name: 'Administrace', exact: true }).click();

  for (const group of ['Škola', 'Výuka', 'Aplikace', 'Archiv']) {
    await expect(page.getByRole('tab', { name: group })).toBeVisible();
  }
  for (const section of [
    'Údaje',
    'Matrika',
    'Třídy',
    'Předměty',
    'Učebny',
    'Osoby',
  ]) {
    await expect(page.getByRole('tab', { name: section })).toBeVisible();
  }
  await expect(page.getByLabel('Ulice')).toBeVisible();

  await page.getByRole('tab', { name: 'Aplikace' }).click();
  await expect(page.getByRole('tab', { name: 'Uživatelé' })).toBeVisible();
  await page.getByRole('tab', { name: 'Upozornění' }).click();

  // The address keeps the section.
  await page.reload();
  await expect(page.getByRole('tab', { name: 'Upozornění' })).toHaveAttribute(
    'aria-selected',
    'true',
  );
  await expect(page.getByText('Upozornění e-mailem a na mobil')).toBeVisible();
});

test('the tab headers stay in sight while the content scrolls', async ({
  page,
}) => {
  await page.setViewportSize({ width: 1200, height: 320 });
  await login(page, 'admin', 'admin123');
  await page.getByRole('link', { name: 'Administrace', exact: true }).click();
  const group = page.getByRole('tab', { name: 'Škola' });
  const section = page.getByRole('tab', { name: 'Předměty' });
  await section.click();
  await expect(
    page.getByRole('button', { name: 'Nový předmět' }),
  ).toBeVisible();

  await page.getByRole('main').hover();
  await page.mouse.wheel(0, 2000);
  await expect(
    page.getByRole('heading', { name: 'Administrace' }),
  ).not.toBeInViewport();
  await expect(group).toBeInViewport();
  await expect(section).toBeInViewport();
  const groupBox = await group.boundingBox();
  expect((await section.boundingBox())!.y).toBeGreaterThan(groupBox!.y + 20);

  // On a phone, under the top bar.
  await page.setViewportSize({ width: 390, height: 320 });
  await page.mouse.wheel(0, 2000);
  await expect(group).toBeInViewport();
  const topBar = await page.locator('.app-topbar').boundingBox();
  expect((await group.boundingBox())!.y).toBeGreaterThanOrEqual(
    topBar!.y + topBar!.height - 1,
  );
});

test('admin filters users by name and contact', async ({ page }) => {
  await login(page, 'admin', 'admin123');
  await page.getByRole('link', { name: 'Administrace', exact: true }).click();
  await page.getByRole('tab', { name: 'Aplikace' }).click();
  await page.getByRole('tab', { name: 'Uživatelé' }).click();

  // Inactive tab panels stay in the DOM (the Osoby one has its own "Jméno"),
  // so scope everything to the Uživatelé panel.
  const panel = page.getByRole('tabpanel', { name: 'Uživatelé' });
  const rows = panel.locator('table tbody tr');
  // Without diacritics on purpose — the search folds them.
  await panel.getByLabel('Jméno', { exact: true }).fill('ucitelova');
  await expect(rows.filter({ hasText: 'Jana Učitelová' })).toHaveCount(1);
  await expect(rows.filter({ hasText: 'Petr Novák' })).toHaveCount(0);

  await panel.getByLabel('Jméno', { exact: true }).fill('');
  await panel.getByLabel('Kontakt', { exact: true }).fill('neexistuje@nikde');
  await expect(rows.first()).toHaveText(/Žádní uživatelé/);
});

test('class teacher sees their own class in two tabs', async ({ page }) => {
  // Demo "ucitel" account is the class teacher (ClassTeacherId) of 5.A.
  await login(page, 'ucitel', 'ucitel123');
  await page.getByRole('link', { name: 'Administrace', exact: true }).click();

  await expect(page.getByText('Jste třídní učitel')).toBeVisible();
  await expect(page.getByRole('tab', { name: 'Předměty' })).toHaveCount(0);
  await expect(page.getByRole('tab', { name: 'Moje třída' })).toBeVisible();
  await expect(page.getByRole('tab', { name: 'Žáci' })).toBeVisible();
  // 5.A's demo student, not any student from an unrelated class — appears
  // twice (Studenti table + the "Rodiče" tooltip title), so assert on the
  // first hit.
  await expect(page.getByText('Jan Novák').first()).toBeVisible();

  await page.getByRole('tab', { name: 'Rodiče' }).click();
  await expect(page.getByRole('cell', { name: 'Petr Novák' })).toBeVisible();

  // "Přidat" opens the assign dialog; nothing to save until a parent is
  // picked.
  await page.getByRole('button', { name: 'Přidat' }).click();
  const dialog = page.getByRole('dialog', { name: 'Přiřadit rodiče' });
  await expect(dialog).toBeVisible();
  await expect(dialog.getByRole('button', { name: 'Uložit' })).toBeDisabled();
});

test('parent is denied the Administrace page', async ({ page }) => {
  await login(page, 'rodic', 'rodic123');

  await expect(
    page.getByRole('link', { name: 'Administrace', exact: true }),
  ).toHaveCount(0);
  await page.goto('/skola/admin');
  await expect(page.getByText('Tato stránka je dostupná')).toBeVisible();
});

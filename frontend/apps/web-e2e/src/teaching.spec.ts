import { Page } from '@playwright/test';
import { expect, test } from './fixtures';

async function openTeaching(page: Page, username: string, password: string) {
  await page.goto('/skola/login');
  await page.locator('#username').fill(username);
  await page.locator('#password').fill(password);
  await page.getByRole('button', { name: 'Přihlásit se' }).click();
  await expect(page).toHaveURL(/\/skola\/home$/);
  await page
    .getByRole('navigation', { name: 'Hlavní navigace' })
    .getByRole('link', { name: 'Rozvrh', exact: true })
    .click();
  await page.getByRole('button', { name: 'Přidělení výuky' }).click();
  await expect(
    page.getByRole('heading', { name: 'Přidělení výuky' }),
  ).toBeVisible();
}

// The seeded 5.A has maths with Jana Učitelová in its timetable; reading
// the plan from the timetable is idempotent, so a rerun finds it the same.
test.describe.serial('teaching', () => {
  test('admin reads the plan and assignments from the timetable', async ({
    page,
  }) => {
    await openTeaching(page, 'admin', 'admin123');
    await page.getByRole('button', { name: 'Načíst z rozvrhu' }).click();
    await expect(
      page.getByText('Plán a přidělení jsou doplněné z rozvrhu.'),
    ).toBeVisible();

    const maths = page
      .getByRole('region', { name: 'Třída 5.A' })
      .getByRole('row')
      .filter({ hasText: 'Matematika' });
    await expect(maths.getByRole('cell').nth(1)).not.toHaveText('—');
    await expect(
      maths.getByRole('combobox', { name: 'Učitel — 5.A Matematika' }),
    ).toContainText('Jana Učitelová');
  });

  test('admin saves the učební plán', async ({ page }) => {
    await openTeaching(page, 'admin', 'admin123');
    await page.getByRole('tab', { name: 'Učební plán' }).click();
    const english = page.getByRole('spinbutton', {
      name: 'Anglický jazyk — 5. ročník',
    });
    await english.fill('3');
    await page.getByRole('button', { name: 'Uložit plán' }).click();
    await expect(page.getByText('Učební plán je uložený.')).toBeVisible();
    await expect(english).toHaveValue('3');
  });

  test('a teacher sees only their own, without editing', async ({ page }) => {
    await openTeaching(page, 'ucitel', 'ucitel123');
    await expect(
      page.getByRole('button', { name: 'Načíst z rozvrhu' }),
    ).toHaveCount(0);
    await page.getByRole('tab', { name: 'Podle učitelů' }).click();
    const rows = page.getByRole('tabpanel').getByRole('row');
    // The header and the teacher's own row.
    await expect(rows).toHaveCount(2);
    await expect(rows.nth(1)).toContainText('Jana Učitelová');
  });
});

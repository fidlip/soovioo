import { Page } from '@playwright/test';
import { expect, test } from './fixtures';

async function login(page: Page, username: string, password: string) {
  await page.goto('/skola/login');
  await page.locator('#username').fill(username);
  await page.locator('#password').fill(password);
  await page.getByRole('button', { name: 'Přihlásit se' }).click();
  await expect(page).toHaveURL(/\/skola\/home$/);
}

function openNav(page: Page, name: string) {
  return page
    .getByRole('navigation', { name: 'Hlavní navigace' })
    .getByRole('link', { name, exact: true })
    .click();
}

async function openTeaching(page: Page) {
  await login(page, 'admin', 'admin123');
  await openNav(page, 'Administrace');
  await page.getByRole('tab', { name: 'Přidělení výuky' }).click();
  await expect(
    page.getByRole('button', { name: 'Načíst z rozvrhu' }),
  ).toBeVisible();
}

// The seeded 5.A has maths with Jana Učitelová in its timetable; reading
// the plan from the timetable is idempotent, so a rerun finds it the same.
test.describe.serial('teaching', () => {
  test('admin reads the plan and assignments from the timetable', async ({
    page,
  }) => {
    await openTeaching(page);
    await page.getByRole('button', { name: 'Načíst z rozvrhu' }).click();
    await expect(
      page.getByText('Plán a přidělení jsou doplněné z rozvrhu.'),
    ).toBeVisible();

    const maths = page
      .getByRole('region', { name: 'Třída 5.A' })
      .getByRole('row')
      .filter({ hasText: 'Matematika' });
    await expect(
      maths.getByRole('spinbutton', { name: 'Plán — 5.A Matematika' }),
    ).not.toHaveValue('');
    await expect(
      maths.getByRole('combobox', { name: 'Učitel — 5.A Matematika' }),
    ).toContainText('Jana Učitelová');
  });

  test('admin saves the učební plán', async ({ page }) => {
    await openTeaching(page);
    await page.getByRole('tab', { name: 'Učební plán' }).click();
    const english = page.getByRole('spinbutton', {
      name: 'Anglický jazyk — 5. ročník',
    });
    await english.fill('3');
    await page.getByRole('button', { name: 'Uložit plán' }).click();
    await expect(page.getByText('Učební plán je uložený.')).toBeVisible();
    await expect(english).toHaveValue('3');
  });

  test('a class opened from Administrace gets its own plan and back', async ({
    page,
  }) => {
    await login(page, 'admin', 'admin123');
    await openNav(page, 'Administrace');
    await page.getByRole('button', { name: 'Přidělení výuky — 5.A' }).click();
    await expect(
      page.getByRole('tab', { name: 'Přidělení výuky' }),
    ).toHaveAttribute('aria-selected', 'true');
    await expect(page.getByRole('region', { name: /^Třída / })).toHaveCount(1);

    const english = page
      .getByRole('region', { name: 'Třída 5.A' })
      .getByRole('row')
      .filter({ hasText: 'Anglický jazyk' });
    const hours = english.getByRole('spinbutton', {
      name: 'Plán — 5.A Anglický jazyk',
    });
    await hours.fill('2');
    await hours.press('Enter');
    await expect(english.getByText('vlastní · ročník 3 h')).toBeVisible();

    await english
      .getByRole('button', { name: 'Podle ročníku — 5.A Anglický jazyk' })
      .click();
    await expect(english.getByText(/vlastní/)).toHaveCount(0);
    await expect(hours).toHaveValue('3');
  });

  test('a teacher sees their own in Rozvrh and Profil, not the agenda', async ({
    page,
  }) => {
    await login(page, 'ucitel', 'ucitel123');
    await openNav(page, 'Rozvrh');
    await expect(page.getByTestId('teacher-teaching')).toContainText(
      /Přiděleno .+ z 22 h nasmlouvaných: .*5\.A M/,
    );

    await page.goto('/skola/settings');
    const teaching = page.getByRole('region', { name: 'Výuka' });
    await expect(teaching).toContainText(/Aprobace: .*M/);
    await expect(teaching).toContainText('Nasmlouvané hodiny: 22 h týdně');
    await expect(teaching).toContainText('5.A M');

    await openNav(page, 'Administrace');
    await expect(page.getByRole('tab', { name: 'Rodiče' })).toBeVisible();
    await expect(
      page.getByRole('tab', { name: 'Přidělení výuky' }),
    ).toHaveCount(0);
  });
});

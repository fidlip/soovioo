import { Page } from '@playwright/test';
import { expect, test } from './fixtures';
import {
  archive,
  draftCopy,
  pickVersion,
  versionsApi,
  versionsButton,
} from './timetable-versions';

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

async function openTeaching(page: Page, version: string) {
  await login(page, 'admin', 'admin123');
  await openNav(page, 'Administrace');
  await page.getByRole('tab', { name: 'Výuka' }).click();
  // Škola has its own Třídy, until Výuka opens.
  await page.getByRole('tab', { name: 'Učební plán' }).waitFor();
  await pickVersion(page, version);
  await page.getByRole('tab', { name: 'Třídy' }).click();
  await expect(
    page.getByRole('button', { name: 'Načíst z rozvrhu' }),
  ).toBeVisible();
}

// The seeded 5.A has maths with Jana Učitelová in its timetable; the plan and
// přidělení change only in a draft, here a copy of what applies.
test.describe.serial('teaching', () => {
  const version = `E2E výuka ${Date.now()}`;
  let versionId = 0;

  test('a published version only shows them', async ({ page, request }) => {
    await login(page, 'admin', 'admin123');
    versionId = await draftCopy(
      await versionsApi(page, request),
      version,
      6000,
    );
    await openNav(page, 'Administrace');
    await page.getByRole('tab', { name: 'Výuka' }).click();
    // Škola has its own Třídy, until Výuka opens.
    await page.getByRole('tab', { name: 'Učební plán' }).waitFor();
    await page.getByRole('tab', { name: 'Třídy' }).click();
    await expect(page.getByRole('region', { name: 'Třída 5.A' })).toBeVisible();
    await expect(
      page.getByRole('button', { name: 'Načíst z rozvrhu' }),
    ).toHaveCount(0);
    await expect(page.getByTestId('teaching-read-only')).toBeVisible();
    await page.getByRole('tab', { name: 'Učební plán' }).click();
    await expect(page.getByTestId('teaching-read-only')).toBeVisible();
    // Skupiny say their own: there the pupils do change.
    await page.getByRole('tab', { name: 'Skupiny' }).click();
    await expect(page.getByTestId('groups-members-only')).toBeVisible();
    await expect(page.getByTestId('teaching-read-only')).toHaveCount(0);
    await expect(
      page.getByRole('spinbutton', { name: 'Plán — 5.A Matematika' }),
    ).toHaveCount(0);
  });

  test('admin reads the plan and assignments from the timetable', async ({
    page,
  }) => {
    await openTeaching(page, version);
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
    await openTeaching(page, version);
    await page.getByRole('tab', { name: 'Učební plán' }).click();
    const english = page.getByRole('spinbutton', {
      name: 'Anglický jazyk — 5. ročník',
    });
    await english.fill('3');
    await page.getByRole('button', { name: 'Uložit plán' }).click();
    await expect(page.getByText('Učební plán je uložený.')).toBeVisible();
    await expect(english).toHaveValue('3');
  });

  test('one version choice for the sections that belong to it', async ({
    page,
  }) => {
    const button = versionsButton(page);
    // Chosen in Rozvrh, then here in Třídy.
    await openTeaching(page, version);
    await expect(button).toContainText(version);
    // Against the page heading: the page may be scrolled differently.
    const heading = page.getByRole('heading', { name: 'Administrace' });
    const place = async () => {
      const box = await button.boundingBox();
      const top = await heading.boundingBox();
      return box && top ? { x: box.x - top.x, y: box.y - top.y } : null;
    };
    const row = await place();
    expect(row).not.toBeNull();

    await page.getByRole('tab', { name: 'Učitelé' }).click();
    await pickVersion(page, 'Platí');
    const label = (await button.getAttribute('aria-label')) ?? '';
    const seeded = label.replace('Verze rozvrhu: ', '');
    await page.getByRole('tab', { name: 'Rozvrh' }).click();
    await expect(page.getByTestId('timetable-version-info')).toContainText(
      seeded,
    );
    await expect(button).toContainText(seeded);
    // In the same place in every section.
    expect(await place()).toEqual(row);

    await expect(page.getByTestId('teaching-sections-divider')).toBeVisible();
    for (const global of ['Suplování', 'Nastavení']) {
      await page.getByRole('tab', { name: global }).click();
      await expect(button).toHaveCount(0);
    }

    await page.getByRole('tab', { name: 'Učební plán' }).click();
    await expect(button).toContainText(seeded);
  });

  test('a class opened from Administrace gets its own plan and back', async ({
    page,
  }) => {
    await login(page, 'admin', 'admin123');
    await openNav(page, 'Administrace');
    await page.getByRole('tab', { name: 'Třídy' }).click();
    await page.getByRole('button', { name: 'Přidělení výuky — 5.A' }).click();
    await expect(page.getByRole('tab', { name: 'Výuka' })).toHaveAttribute(
      'aria-selected',
      'true',
    );
    await pickVersion(page, version);
    await expect(page.getByRole('region', { name: /^Třída / })).toHaveCount(1);
    await expect(page.getByTestId('teaching-read-only')).toHaveCount(0);

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
    request,
  }) => {
    await login(page, 'ucitel', 'ucitel123');
    await archive(await versionsApi(page, request), versionId);
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
    await expect(page.getByRole('tab', { name: 'Výuka' })).toHaveCount(0);
  });
});

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

async function openGroups(page: Page) {
  await login(page, 'admin', 'admin123');
  await openNav(page, 'Administrace');
  await page.getByRole('tab', { name: 'Přidělení výuky' }).click();
  await page.getByRole('tab', { name: 'Skupiny' }).click();
  await expect(
    page.getByRole('button', { name: 'Nová skupina' }),
  ).toBeVisible();
}

/** The grid cell of one day (row, by its full day name) and period. */
function cell(page: Page, day: string, period: string) {
  return page
    .locator('tbody tr')
    .filter({ has: page.locator(`th[title="${day}"]`) })
    .locator(`td[data-period="${period}"]`);
}

async function pick(
  page: Page,
  combobox: ReturnType<Page['getByRole']>,
  option: string,
) {
  await combobox.click();
  const list = page.locator(`#${await combobox.getAttribute('aria-controls')}`);
  await list.getByRole('option', { name: option, exact: true }).click();
}

const GROUP = 'Seminář e2e';

// A group of Jan Novák alone goes through its life: made in Administrace,
// given a lesson in Rozvrh, seen by Jan, deleted with its lesson — so the
// shared data end as they started.
test.describe.serial('student groups', () => {
  const slot = { day: '', period: '' };

  test('admin makes a group of a pupil of 5.A', async ({ page }) => {
    await openGroups(page);
    await page.getByRole('button', { name: 'Nová skupina' }).click();
    const dialog = page.getByRole('dialog', { name: 'Nová skupina' });
    await dialog.getByLabel('Název').fill(GROUP);
    await pick(
      page,
      dialog.getByRole('combobox', { name: 'Předmět' }),
      'Anglický jazyk',
    );
    await dialog.locator('p-multiselect[name="groupClasses"]').click();
    await page.getByRole('option', { name: '5.A', exact: true }).click();
    await page.keyboard.press('Escape');
    await dialog.locator('p-multiselect[name="groupStudents"]').click();
    await page.getByRole('option', { name: /^Novák Jan/ }).click();
    await page.keyboard.press('Escape');
    await expect(dialog.getByText('Vybráno 1.')).toBeVisible();
    // Its own hours: Aj of 5.A in the plan is up to the other tests.
    await dialog.getByRole('spinbutton', { name: 'Hodiny týdně' }).fill('1');
    await dialog.getByRole('button', { name: 'Uložit' }).click();
    await expect(dialog).toBeHidden();

    const row = page
      .getByRole('region', { name: 'Samostatné skupiny' })
      .getByRole('row')
      .filter({ hasText: GROUP });
    await expect(row).toContainText('Anglický jazyk');
    await expect(row).toContainText('V rozvrhu chybí 1 h.');
  });

  test('admin gives the group a lesson in Rozvrh', async ({ page }) => {
    await login(page, 'admin', 'admin123');
    await openNav(page, 'Rozvrh');
    await page.locator('p-select[name="classPicker"]').click();
    await page.getByRole('option', { name: '5.A', exact: true }).click();
    await page.getByRole('button', { name: 'Upravit rozvrh' }).click();

    // The last free slot: the timetable lesson test takes the first.
    const addButton = page
      .getByRole('button', { name: 'Přidat hodinu', exact: true })
      .last();
    const target = await addButton.evaluate((button) => {
      const td = button.closest('td') as HTMLElement;
      const th = td.closest('tr')?.querySelector('th');
      return { day: th?.title ?? '', period: td.dataset['period'] ?? '' };
    });
    slot.day = target.day;
    slot.period = target.period;
    await cell(page, slot.day, slot.period).hover();
    await addButton.click();

    const dialog = page.getByRole('dialog', { name: 'Nová hodina' });
    await pick(page, dialog.getByRole('combobox', { name: 'Pro' }), GROUP);
    await expect(
      dialog.getByRole('combobox', { name: 'Předmět' }),
    ).toContainText('Anglický jazyk');
    await dialog.locator('button[type="submit"]').click();
    await expect(dialog).toBeHidden();
    await expect(cell(page, slot.day, slot.period)).toContainText(`(${GROUP})`);
  });

  test('the pupil sees the lesson of their group', async ({ page }) => {
    await login(page, 'zak', 'zak123');
    await openNav(page, 'Rozvrh');
    await expect(cell(page, slot.day, slot.period)).toContainText(`(${GROUP})`);
  });

  test('admin deletes the group with its lesson', async ({ page }) => {
    await openGroups(page);
    await page.getByRole('button', { name: `Smazat skupinu ${GROUP}` }).click();
    await page
      .getByRole('alertdialog', { name: 'Smazat skupinu' })
      .getByRole('button', { name: 'Smazat' })
      .click();
    await expect(
      page.getByRole('button', { name: `Smazat skupinu ${GROUP}` }),
    ).toHaveCount(0);

    await openNav(page, 'Rozvrh');
    await page.locator('p-select[name="classPicker"]').click();
    await page.getByRole('option', { name: '5.A', exact: true }).click();
    // A period without lessons isn't shown at all: the whole grid then.
    await expect(page.locator('tbody td[data-period]').first()).toBeVisible();
    await expect(page.locator('tbody')).not.toContainText(GROUP);
  });
});

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
  await page
    .getByRole('navigation', { name: 'Hlavní navigace' })
    .getByRole('link', { name: 'Rozvrh', exact: true })
    .click();
  await expect(page).toHaveURL(/\/skola\/timetable$/);
}

/** The grid cell of one day (row, by its full day name) and period. */
function cell(
  page: import('@playwright/test').Page,
  day: string,
  period: string,
) {
  return page
    .locator('tbody tr')
    .filter({ has: page.locator(`th[title="${day}"]`) })
    .locator(`td[data-period="${period}"]`);
}

/** A teacher opens on their own timetable; lessons are added per class —
 * Jan Novák's, whose parent then checks it. The class is looked up in
 * Administrace (ucitel is its class teacher) rather than hard-coded: the
 * seed calls it 5.A, but a local database may have it renamed. */
async function showJansClass(page: import('@playwright/test').Page) {
  await page
    .getByRole('navigation', { name: 'Hlavní navigace' })
    .getByRole('link', { name: 'Administrace', exact: true })
    .click();
  const className = (
    await page
      .getByRole('row')
      .filter({ hasText: 'Jan Novák' })
      .first()
      .getByRole('cell')
      .nth(1)
      .innerText()
  ).trim();
  await page
    .getByRole('navigation', { name: 'Hlavní navigace' })
    .getByRole('link', { name: 'Rozvrh', exact: true })
    .click();

  await page.locator('p-select[name="viewMode"]').click();
  await page.getByRole('option', { name: 'Třída', exact: true }).click();
  await page.locator('p-select[name="classPicker"]').click();
  await page.getByRole('option', { name: className, exact: true }).click();
}

// One lesson goes through its life: the teacher adds it to Jan Novák's
// class in a free slot, Jan's parent sees it, the teacher removes
// it again — so the shared timetable ends up as it started.
test.describe.serial('timetable lesson', () => {
  const slot = { day: '', period: '', text: '' };

  test('teacher adds a lesson to a free slot', async ({ page }) => {
    await login(page, 'ucitel', 'ucitel123');

    await showJansClass(page);
    await page.getByRole('button', { name: 'Upravit rozvrh' }).click();

    const addButton = page
      .getByRole('button', { name: 'Přidat hodinu' })
      .first();
    const target = await addButton.evaluate((button) => {
      const td = button.closest('td') as HTMLElement;
      const th = td.closest('tr')?.querySelector('th');
      return { day: th?.title ?? '', period: td.dataset['period'] ?? '' };
    });
    slot.day = target.day;
    slot.period = target.period;
    // Hover first: the button is only visible on hover (or focus).
    await cell(page, slot.day, slot.period).hover();
    await addButton.click();

    const dialog = page.getByRole('dialog', { name: 'Nová hodina' });
    await dialog.locator('p-select[name="formSubjectId"]').click();
    await page.getByRole('option').first().click();
    await dialog.locator('button[type="submit"]').click();
    await expect(dialog).toBeHidden();

    await expect(
      cell(page, slot.day, slot.period).getByRole('button', {
        name: 'Smazat hodinu',
      }),
    ).toHaveCount(1);
    slot.text = (
      await cell(page, slot.day, slot.period)
        .locator('div.font-semibold')
        .innerText()
    ).trim();
    expect(slot.text).not.toBe('');
  });

  test('parent sees it and cannot edit the timetable', async ({ page }) => {
    await login(page, 'rodic', 'rodic123');

    await expect(
      page.getByRole('button', { name: 'Upravit rozvrh' }),
    ).toHaveCount(0);
    await expect(cell(page, slot.day, slot.period)).toContainText(slot.text);
  });

  test('teacher deletes the lesson', async ({ page }) => {
    await login(page, 'ucitel', 'ucitel123');

    await showJansClass(page);
    await page.getByRole('button', { name: 'Upravit rozvrh' }).click();
    await cell(page, slot.day, slot.period).hover();
    await cell(page, slot.day, slot.period)
      .getByRole('button', { name: 'Smazat hodinu' })
      .click();
    await page
      .getByRole('alertdialog', { name: 'Smazat hodinu' })
      .getByRole('button', { name: 'Smazat' })
      .click();

    await expect(
      cell(page, slot.day, slot.period).getByRole('button', {
        name: 'Přidat hodinu',
      }),
    ).toHaveCount(1);
  });
});

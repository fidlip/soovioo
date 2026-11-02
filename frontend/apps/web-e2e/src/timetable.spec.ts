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

/** Jan Novák's class, whose parent then checks the lesson — looked up in
 * the class teacher's Administrace (ucitel's) rather than hard-coded: the
 * seed calls it 5.A, but a local database may have it renamed. */
async function jansClassName(
  page: import('@playwright/test').Page,
): Promise<string> {
  await page
    .getByRole('navigation', { name: 'Hlavní navigace' })
    .getByRole('link', { name: 'Administrace', exact: true })
    .click();
  return (
    await page
      .getByRole('row')
      .filter({ hasText: 'Jan Novák' })
      .first()
      .getByRole('cell')
      .nth(1)
      .innerText()
  ).trim();
}

/** Lessons are added per class, by the admin. */
async function showClass(
  page: import('@playwright/test').Page,
  className: string,
) {
  await page.locator('p-select[name="viewMode"]').click();
  await page.getByRole('option', { name: 'Třída', exact: true }).click();
  await page.locator('p-select[name="classPicker"]').click();
  await page.getByRole('option', { name: className, exact: true }).click();
}

// One lesson goes through its life: the admin adds it to Jan Novák's
// class in a free slot, Jan's parent sees it, the admin removes it again —
// so the shared timetable ends up as it started. A teacher only looks.
test.describe.serial('timetable lesson', () => {
  const slot = { day: '', period: '', text: '', className: '' };

  test('a teacher sees the timetable but does not edit it', async ({
    page,
  }) => {
    await login(page, 'ucitel', 'ucitel123');
    await expect(
      page.getByRole('button', { name: 'Upravit rozvrh' }),
    ).toHaveCount(0);
    slot.className = await jansClassName(page);
  });

  test('admin adds a lesson to a free slot', async ({ page }) => {
    await login(page, 'admin', 'admin123');

    await showClass(page, slot.className);
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

  test('admin deletes the lesson', async ({ page }) => {
    await login(page, 'admin', 'admin123');

    await showClass(page, slot.className);
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

function inDays(days: number): string {
  const date = new Date();
  date.setDate(date.getDate() + days);
  const pad = (n: number) => `${n}`.padStart(2, '0');
  return `${pad(date.getDate())}.${pad(date.getMonth() + 1)}.${date.getFullYear()}`;
}

// A version goes through its life: the admin prepares a draft as a copy,
// a parent doesn't see it, the admin publishes it far ahead and archives it
// again — so the timetable that applies stays as it was.
test.describe.serial('timetable version', () => {
  const name = `E2E verze ${Date.now()}`;

  test('admin prepares a draft as a copy of the current timetable', async ({
    page,
  }) => {
    await login(page, 'admin', 'admin123');
    await page.getByRole('button', { name: 'Verze rozvrhu' }).click();
    const dialog = page.getByRole('dialog', { name: 'Verze rozvrhu' });
    await dialog.getByRole('button', { name: 'Nový koncept' }).click();
    await dialog.getByLabel('Název').fill(name);
    // Typed key by key — the DatePicker ignores a pasted value.
    await dialog
      .getByLabel('Platí od')
      .pressSequentially(inDays(400 + Math.floor(Math.random() * 300)));
    await dialog.getByRole('button', { name: 'Vytvořit koncept' }).click();

    // The new draft is what the page shows now.
    await expect(dialog).toBeHidden();
    const info = page.getByTestId('timetable-version-info');
    await expect(info).toContainText(name);
    await expect(info).toContainText('Koncept');
  });

  test('a parent does not see the draft', async ({ page }) => {
    await login(page, 'rodic', 'rodic123');
    await expect(page.getByText(name)).toHaveCount(0);
    await expect(page.locator('p-select[name="versionPicker"]')).toHaveCount(0);
  });

  test('admin publishes it ahead and archives it again', async ({ page }) => {
    await login(page, 'admin', 'admin123');
    await page.getByRole('button', { name: 'Verze rozvrhu' }).click();
    const dialog = page.getByRole('dialog', { name: 'Verze rozvrhu' });
    const version = dialog
      .getByTestId('timetable-version')
      .filter({ hasText: name });

    await version.getByRole('button', { name: 'Zveřejnit' }).click();
    await page
      .getByRole('alertdialog', { name: 'Zveřejnit verzi rozvrhu' })
      .getByRole('button', { name: 'Zveřejnit' })
      .click();
    await expect(version).toContainText('Naplánovaná');

    await version.getByRole('button', { name: 'Archivovat' }).click();
    await page
      .getByRole('alertdialog', { name: 'Archivovat verzi rozvrhu' })
      .getByRole('button', { name: 'Archivovat' })
      .click();
    await expect(version).toContainText('Archivovaná');
    await expect(
      version.getByRole('button', { name: 'Vrátit mezi koncepty' }),
    ).toBeVisible();
  });
});

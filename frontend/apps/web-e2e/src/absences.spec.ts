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
  await page.getByRole('link', { name: 'Docházka', exact: true }).click();
  await expect(page).toHaveURL(/\/skola\/absences$/);
}

/** A random past date, so this run's absence is the only row with it — even
 * against a local database where earlier runs (or other spec files running
 * in parallel) left absences behind. */
function randomPastDate(): { shown: RegExp; typed: string } {
  const year = 2001 + Math.floor(Math.random() * 20);
  const month = 1 + Math.floor(Math.random() * 12);
  const day = 1 + Math.floor(Math.random() * 28);
  const pad = (n: number) => `${n}`.padStart(2, '0');
  return {
    // "1. 2. 2003", but not inside "21. 2. 2003".
    shown: new RegExp(`(^|\\D)${day}\\. ${month}\\. ${year}`),
    typed: `${pad(day)}.${pad(month)}.${year}`,
  };
}

// One absence goes through its whole life: the teacher records it, the
// parent excuses it, the teacher approves it — each step depends on the
// previous one.
test.describe.serial('absence excuse workflow', () => {
  const date = randomPastDate();
  const row = (page: import('@playwright/test').Page) =>
    page
      .locator('table tbody tr[id^="absence-"]')
      .filter({ hasText: date.shown });

  test('teacher records an absence', async ({ page }) => {
    await login(page, 'ucitel', 'ucitel123');

    await page.getByRole('button', { name: 'Zapsat absenci' }).click();
    const dialog = page.getByRole('dialog', { name: 'Zapsat absenci' });
    // Focused by the dialog once its open animation ends — wait for it, so
    // it can't move focus away while the date is typed below.
    await expect(dialog.locator('#absenceStudent')).toBeFocused();
    // The dialog preselects no student; Jan Novák is 5.A's demo student.
    await dialog.locator('#absenceStudent').click();
    await page.getByRole('option', { name: 'Jan Novák' }).click();
    // Typed key by key, not fill(): the date picker parses only input that
    // follows a keydown, a filled-in value reverts on blur.
    const dateInput = dialog.locator('#absenceDate');
    await dateInput.selectText();
    await dateInput.pressSequentially(date.typed);
    await dateInput.press('Escape');
    // The only `type="submit"` button on the page (see grades.spec.ts on
    // why not by its accessible name).
    await dialog.locator('button[type="submit"]').click();

    await expect(row(page)).toHaveCount(1);
    await expect(row(page)).toContainText('Jan Novák');
    await expect(row(page)).toContainText('Neomluveno');
  });

  test('parent sees it, cannot edit it, and excuses it', async ({ page }) => {
    await login(page, 'rodic', 'rodic123');
    // A family starts on Omluvenky.
    await expect(page.getByRole('tab', { name: 'Omluvenky' })).toHaveAttribute(
      'aria-selected',
      'true',
    );
    await page.getByRole('tab', { name: 'Absence' }).click();

    await expect(
      page.getByRole('button', { name: 'Zapsat absenci' }),
    ).toHaveCount(0);
    await expect(row(page)).toHaveCount(1);
    await expect(
      row(page).getByRole('button', { name: 'Upravit' }),
    ).toHaveCount(0);
    await expect(row(page).getByRole('button', { name: 'Smazat' })).toHaveCount(
      0,
    );

    await row(page).getByRole('button', { name: 'Omluvit' }).click();
    const dialog = page.getByRole('dialog', { name: 'Omluvit' });
    await dialog.locator('#excuseReason').fill('Návštěva lékaře');
    await dialog.locator('button[type="submit"]').click();
    await expect(dialog).toBeHidden();

    await expect(row(page)).toContainText('Čeká na schválení');
    await expect(row(page)).toContainText('Návštěva lékaře');
  });

  test('student sees it but cannot approve it', async ({ page }) => {
    await login(page, 'zak', 'zak123');
    await page.getByRole('tab', { name: 'Absence' }).click();

    await expect(row(page)).toContainText('Čeká na schválení');
    await expect(
      row(page).getByRole('button', { name: 'Schválit' }),
    ).toHaveCount(0);
    await expect(
      row(page).getByRole('button', { name: 'Omluvit' }),
    ).toHaveCount(0);
  });

  test('teacher approves the excuse and deletes the record', async ({
    page,
  }) => {
    await login(page, 'ucitel', 'ucitel123');

    await row(page).getByRole('button', { name: 'Schválit' }).click();
    await expect(row(page)).toContainText('Omluveno');

    // Clean up — the random date keeps reruns apart, but there's no need
    // to leave the record in a developer's local database.
    await row(page).getByRole('button', { name: 'Smazat' }).click();
    await expect(row(page)).toHaveCount(0);
  });
});

/** A random Monday and the Tuesday after it, as typed in the date picker:
 * in the past, or (`future`) years ahead. */
function randomMondayAndTuesday(future = false): [string, string] {
  const date = new Date(
    (future ? 2080 : 2001) + Math.floor(Math.random() * 15),
    Math.floor(Math.random() * 12),
    1 + Math.floor(Math.random() * 28),
  );
  date.setDate(date.getDate() - ((date.getDay() + 6) % 7));
  const typed = (d: Date) =>
    `${`${d.getDate()}`.padStart(2, '0')}.${`${d.getMonth() + 1}`.padStart(2, '0')}.${d.getFullYear()}`;
  const tuesday = new Date(date);
  tuesday.setDate(date.getDate() + 1);
  return [typed(date), typed(tuesday)];
}

async function typeDate(
  input: import('@playwright/test').Locator,
  value: string,
) {
  await input.selectText();
  await input.pressSequentially(value);
  await input.press('Escape');
}

/** The parent writes an omluvenka for two days on Omluvenky. */
async function writeExcuse(
  page: import('@playwright/test').Page,
  [from, to]: [string, string],
  reason: string,
) {
  await page.getByRole('button', { name: 'Omluvit' }).click();
  const dialog = page.getByRole('dialog', { name: 'Omluvit' });
  await expect(dialog.locator('#excuseStudent')).toContainText('Jan Novák');
  await typeDate(dialog.locator('#excuseFrom'), from);
  // Do follows Od; typed over once it has.
  await expect(dialog.locator('#excuseTo')).toHaveValue(from);
  await typeDate(dialog.locator('#excuseTo'), to);
  await expect(dialog.locator('#excuseTo')).toHaveValue(to);
  await dialog.locator('#excuseReason').fill(reason);
  return dialog;
}

// An omluvenka for two days goes its way: the parent writes it with a
// doctor's note and edits it, the teacher rejects it with a reason the
// parent then sees.
test.describe.serial('parent writes an omluvenka', () => {
  const days = randomMondayAndTuesday();
  const token = `${Date.now()}${Math.floor(Math.random() * 1000)}`;
  const excuse = (page: import('@playwright/test').Page) =>
    page.locator('tr[id^="excuse-"]').filter({ hasText: token });

  test('parent writes it with a file and edits the reason', async ({
    page,
  }) => {
    await login(page, 'rodic', 'rodic123');

    const dialog = await writeExcuse(page, days, `Hory ${token}`);
    await dialog.locator('#excuseFile').setInputFiles({
      name: 'zprava.pdf',
      mimeType: 'application/pdf',
      buffer: Buffer.from('%PDF-1.4'),
    });
    await expect(dialog).toContainText('zprava.pdf');
    await dialog.locator('button[type="submit"]').click();
    await expect(dialog).toBeHidden();

    await expect(excuse(page)).toContainText('Čeká na schválení');
    await expect(excuse(page)).toContainText('zprava.pdf');

    await excuse(page).getByRole('button', { name: 'Upravit' }).click();
    const edit = page.getByRole('dialog', { name: 'Upravit omluvenku' });
    await edit.locator('#excuseReason').fill(`Chřipka ${token}`);
    await edit.locator('button[type="submit"]').click();
    await expect(excuse(page)).toContainText(`Chřipka ${token}`);
  });

  test('teacher rejects it only with a reason', async ({ page }) => {
    await login(page, 'ucitel', 'ucitel123');
    // From a day on Absence, the reason leads to the omluvenka; the clip
    // says it has a file.
    const day = page
      .locator('tr[id^="absence-"]')
      .filter({ hasText: `Chřipka ${token}` })
      .first();
    await expect(day.getByLabel('Omluvenka má přílohu')).toBeVisible();
    await day.getByRole('link', { name: `Chřipka ${token}` }).click();
    await expect(page.getByRole('tab', { name: 'Omluvenky' })).toHaveAttribute(
      'aria-selected',
      'true',
    );
    await expect(excuse(page)).toBeVisible();

    await excuse(page).getByRole('button', { name: 'Zamítnout' }).click();
    const dialog = page.getByRole('dialog', { name: /^Zamítnout omluvenku/ });
    await expect(dialog.locator('button[type="submit"]')).toBeDisabled();
    await dialog.locator('#rejectNote').fill('Chybí lékařská zpráva');
    await dialog.locator('button[type="submit"]').click();

    await expect(excuse(page)).toContainText('Zamítnuta');
    await expect(excuse(page)).toContainText('Chybí lékařská zpráva');
  });

  test('parent sees why, and can no longer change it', async ({ page }) => {
    await login(page, 'rodic', 'rodic123');

    await expect(excuse(page)).toContainText('Zamítnuta');
    await expect(excuse(page)).toContainText('Chybí lékařská zpráva');
    await expect(
      excuse(page).getByRole('button', { name: 'Upravit' }),
    ).toHaveCount(0);
  });

  test('teacher cleans up the days', async ({ page }) => {
    await login(page, 'ucitel', 'ucitel123');
    const rows = page
      .locator('table tbody tr[id^="absence-"]')
      .filter({ hasText: `Chřipka ${token}` });
    await expect(rows).toHaveCount(2);
    for (let i = 0; i < 2; i++) {
      await rows.first().getByRole('button', { name: 'Smazat' }).click();
      await expect(rows).toHaveCount(1 - i);
    }
  });
});

test('an omluvenka ahead is approved at the earliest on its day', async ({
  page,
}) => {
  const token = `${Date.now()}${Math.floor(Math.random() * 1000)}`;
  const excuse = page.locator('tr[id^="excuse-"]').filter({ hasText: token });
  await login(page, 'rodic', 'rodic123');
  const dialog = await writeExcuse(
    page,
    randomMondayAndTuesday(true),
    `Lázně ${token}`,
  );
  await dialog.locator('button[type="submit"]').click();
  await expect(excuse).toContainText('Čeká na schválení');
  await page.context().clearCookies();

  await login(page, 'ucitel', 'ucitel123');
  await page.getByRole('tab', { name: 'Omluvenky' }).click();
  await expect(excuse.getByRole('button', { name: 'Schválit' })).toBeDisabled();
  await page.context().clearCookies();

  // Clean up: the family takes it back.
  await login(page, 'rodic', 'rodic123');
  await excuse.getByRole('button', { name: 'Smazat' }).click();
  await expect(excuse).toHaveCount(0);
});

test('the list says it is loading, not that there is nothing, until the absences arrive', async ({
  page,
}) => {
  let release: () => void = () => undefined;
  const held = new Promise<void>((resolve) => (release = resolve));
  await page.route('**/api/absences', async (route) => {
    await held;
    await route.continue();
  });
  await login(page, 'ucitel', 'ucitel123');

  const table = page.locator('table');
  await expect(table.getByRole('status')).toHaveText('Načítání…');
  await expect(table.getByText('Žádné záznamy.')).toHaveCount(0);
  release();
  await expect(table.getByRole('status')).toHaveCount(0);
});

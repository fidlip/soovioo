import { expect, test } from './fixtures';
import {
  mondaysAndTuesdays,
  pick,
  relativeToToday,
  teachingDays,
  typed,
} from './school-days';

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
  if (username === 'ucitel') {
    // A teacher starts on the třídní kniha.
    const classBook = page.getByRole('tab', { name: 'Třídní kniha' });
    await expect(classBook).toHaveAttribute('aria-selected', 'true');
    await page.getByRole('tab', { name: 'Absence' }).click();
  }
}

/** How a day shows in the list, and as typed in the date picker. */
function shownDate(day: Date): { shown: RegExp; typed: string } {
  return {
    // "1. 2. 2003", but not inside "21. 2. 2003".
    shown: new RegExp(
      `(^|\\D)${day.getDate()}\\. ${day.getMonth() + 1}\\. ${day.getFullYear()}`,
    ),
    typed: typed(day),
  };
}

// One absence goes through its whole life: the teacher records it, the
// parent excuses it, the teacher approves it — each step depends on the
// previous one. On a random past day of lessons (records go only into a
// school year), so this run's absence is likely the only row with it.
test.describe.serial('absence excuse workflow', () => {
  let date = { shown: /$^/, typed: '' };
  test.beforeAll(async ({ playwright }) => {
    // Not a Monday or a Tuesday: the omluvenky below take those.
    const day = pick(
      relativeToToday(await teachingDays({ playwright }), true).filter(
        (d) => d.getDay() > 2,
      ),
    );
    test.skip(!day, 'No past day of lessons in the school year yet.');
    date = shownDate(day as Date);
  });
  const row = (page: import('@playwright/test').Page) =>
    page
      .locator('table tbody tr[id^="absence-"]')
      .filter({ hasText: date.shown });

  test('teacher records an absence', async ({ page }) => {
    await login(page, 'ucitel', 'ucitel123');

    await page.getByRole('button', { name: 'Zapsat absenci' }).click();
    // "Upravit absenci" while the preselected pupil's today has an omluvenka.
    const dialog = page.getByRole('dialog', {
      name: /^(Zapsat|Upravit) absenci$/,
    });
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
    await approveDespiteUnrecorded(page, row(page).getByText('Omluveno'));

    // Clean up — the random date keeps reruns apart, but there's no need
    // to leave the record in a developer's local database.
    await row(page).getByRole('button', { name: 'Smazat' }).click();
    await expect(row(page)).toHaveCount(0);
  });
});

/** Approving days whose lessons the třídní kniha doesn't have asks first —
 * says yes when it does; `done` shows once approved. */
async function approveDespiteUnrecorded(
  page: import('@playwright/test').Page,
  done: import('@playwright/test').Locator,
) {
  const warning = page.getByRole('alertdialog', { name: 'Nezapsané hodiny' });
  await expect(warning.or(done).first()).toBeVisible();
  if (await warning.isVisible()) {
    await warning.getByRole('button', { name: 'Schválit' }).click();
  }
  await expect(done).toBeVisible();
}

/** A random Monday and the Tuesday after it, days of lessons, as typed in
 * the date picker: in the past, or (`future`) ahead. */
async function randomMondayAndTuesday(
  playwright: import('@playwright/test').PlaywrightWorkerArgs['playwright'],
  future = false,
): Promise<[string, string] | undefined> {
  const pair = pick(
    mondaysAndTuesdays(
      relativeToToday(await teachingDays({ playwright }), !future),
    ),
  );
  return pair && [typed(pair[0]), typed(pair[1])];
}

async function typeDate(
  input: import('@playwright/test').Locator,
  value: string,
) {
  await input.selectText();
  await input.pressSequentially(value);
  // Tab, not Escape: with the calendar already closed, Escape closes the dialog.
  await input.press('Tab');
}

/** The parent writes an omluvenka for two days on Omluvenky. */
async function writeExcuse(
  page: import('@playwright/test').Page,
  [from, to]: [string, string],
  reason: string,
) {
  await page.getByRole('button', { name: 'Omluvit' }).click();
  const dialog = page.getByRole('dialog', { name: 'Omluvit' });
  // Focused once the open animation ends — wait, so it can't move focus
  // away while the dates are typed.
  await expect(dialog.locator('#excuseStudent')).toBeFocused();
  await expect(dialog.locator('#excuseStudent')).toContainText('Jan');
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
  let days: [string, string] = ['', ''];
  test.beforeAll(async ({ playwright }) => {
    const pair = await randomMondayAndTuesday(playwright);
    test.skip(!pair, 'No past Monday and Tuesday of lessons in the year yet.');
    days = pair as [string, string];
  });
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

  test('teacher changes the decision and returns it to the family', async ({
    page,
  }) => {
    await login(page, 'ucitel', 'ucitel123');
    await page.getByRole('tab', { name: 'Omluvenky' }).click();

    // The list reloads after a decision (also live), which can re-render the
    // row while its menu is open: then the menu is opened again.
    const decide = async (item: string) => {
      await expect(async () => {
        await page.keyboard.press('Escape');
        await excuse(page)
          .getByRole('button', { name: 'Změnit rozhodnutí' })
          .click();
        await page
          .getByRole('menuitem', { name: item })
          .click({ timeout: 3000 });
      }).toPass({ timeout: 20000 });
    };
    await decide('Schválit');
    await approveDespiteUnrecorded(page, excuse(page).getByText('Schválena'));

    await decide('Vrátit k posouzení');
    await expect(excuse(page)).toContainText('Čeká na schválení');
    await expect(
      excuse(page).getByRole('button', { name: 'Upravit' }),
    ).toBeVisible();

    await page.context().clearCookies();
    await login(page, 'rodic', 'rodic123');
    await expect(
      excuse(page).getByRole('button', { name: 'Upravit' }),
    ).toBeVisible();
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
  playwright,
}) => {
  const days = await randomMondayAndTuesday(playwright, true);
  test.skip(!days, 'No Monday and Tuesday of lessons ahead in the year.');
  const token = `${Date.now()}${Math.floor(Math.random() * 1000)}`;
  const excuse = page.locator('tr[id^="excuse-"]').filter({ hasText: token });
  await login(page, 'rodic', 'rodic123');
  const dialog = await writeExcuse(
    page,
    days as [string, string],
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

  const list = page.locator('app-grouped-table');
  await expect(list.getByRole('status')).toHaveText('Načítání…');
  await expect(list.getByText('Žádné záznamy.')).toHaveCount(0);
  release();
  await expect(list.getByRole('status')).toHaveCount(0);
});

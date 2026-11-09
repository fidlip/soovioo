import { Page } from '@playwright/test';
import { expect, test } from './fixtures';
import {
  discard,
  draftCopy,
  openVersionWeek,
  pickVersion,
  publishQuietly,
  runDayEndJob,
  showVersion,
  unpublish,
  versionsApi,
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

async function openGroups(page: Page, version?: string) {
  await login(page, 'admin', 'admin123');
  await openNav(page, 'Administrace');
  await page.getByRole('tab', { name: 'Výuka' }).click();
  if (version) {
    await pickVersion(page, version);
  }
  await page.getByRole('tab', { name: 'Skupiny' }).click();
  await expect(
    page.getByRole('region', { name: 'Samostatné skupiny' }),
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

const GROUP = 'Skupina e2e';
/** Of the version that applies, seeded with Jan Novák and a lesson with Jana
 * Učitelová: a group can't be made there any more. */
const SEEDED = 'Seminář Aj';

// A group of Jan Novák alone goes through its life in a draft far ahead: made
// in Administrace, given a lesson in Rozvrh, seen by Jan once published, deleted
// with its lesson back among drafts — so the timetable that applies stays as it was.
test.describe.serial('student groups', () => {
  const slot = { day: '', period: '' };
  const version = `E2E skupiny ${Date.now()}`;
  let versionId = 0;

  test('admin makes a group of a pupil of 5.A', async ({ page, request }) => {
    await page.goto('/skola/login');
    versionId = await draftCopy(
      await versionsApi(page, request),
      version,
      2000,
    );
    await openGroups(page, version);
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
    await pick(
      page,
      dialog.getByRole('combobox', { name: 'Učitel' }),
      'Jana Učitelová',
    );
    await dialog.getByRole('button', { name: 'Uložit' }).click();
    await expect(dialog).toBeHidden();

    const row = page
      .getByRole('region', { name: 'Samostatné skupiny' })
      .getByRole('row')
      .filter({ hasText: GROUP });
    await expect(row).toContainText('Anglický jazyk');
    await expect(row).toContainText('V rozvrhu chybí 1 h.');
    await expect(row).toBeInViewport();
  });

  test('admin gives the group a lesson in Rozvrh', async ({ page }) => {
    await login(page, 'admin', 'admin123');
    await openNav(page, 'Rozvrh');
    await page.locator('p-select[name="classPicker"]').click();
    await page.getByRole('option', { name: '5.A', exact: true }).click();
    await page.getByRole('button', { name: 'Upravit rozvrh' }).click();
    await showVersion(page, version);

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

  test('the pupil sees the lesson of their group', async ({
    page,
    request,
  }) => {
    await login(page, 'zak', 'zak123');
    await publishQuietly(await versionsApi(page, request), versionId);
    await openVersionWeek(page, versionId);
    await expect(cell(page, slot.day, slot.period)).toContainText(`(${GROUP})`);
  });

  const homework = `E2E úkol skupiny ${Date.now()}`;
  const card = (page: Page) =>
    page.locator('p-card').filter({ hasText: homework });

  test('its teacher gives the group homework', async ({ page }) => {
    await login(page, 'ucitel', 'ucitel123');
    await openNav(page, 'Domácí úkoly');
    await page.getByRole('button', { name: 'Nový úkol' }).click();
    const dialog = page.getByRole('dialog', { name: 'Nový úkol' });
    await pick(
      page,
      dialog.getByRole('combobox', { name: 'Třída a předmět' }),
      `${SEEDED} (5.A) — Anglický jazyk`,
    );
    await expect(
      dialog.getByText('Komu (prázdné = celá skupina)'),
    ).toBeVisible();
    await dialog.getByLabel(/^Zadání/).fill(homework);
    await dialog.getByText('Vyžadovat potvrzení splnění').click();
    await dialog.locator('button[type="submit"]').click();
    await expect(dialog).toBeHidden();
    await expect(card(page)).toContainText(`${SEEDED} (5.A)`);
    // The group's pupils, not the whole class.
    await expect(card(page)).toContainText('Jan Novák — nesplněno');
  });

  test('the pupil sees the homework of their group', async ({ page }) => {
    await login(page, 'zak', 'zak123');
    await openNav(page, 'Domácí úkoly');
    await expect(card(page)).toHaveCount(1);
  });

  test('its teacher deletes the homework', async ({ page }) => {
    await login(page, 'ucitel', 'ucitel123');
    await openNav(page, 'Domácí úkoly');
    await card(page).getByRole('button', { name: 'Smazat úkol' }).click();
    await expect(card(page)).toHaveCount(0);
  });

  test('admin deletes the group with its lesson back in the draft', async ({
    page,
    request,
  }) => {
    await page.goto('/skola/login');
    const admin = await versionsApi(page, request);
    await unpublish(admin, versionId);
    await openGroups(page, version);
    await page.getByRole('button', { name: `Smazat skupinu ${GROUP}` }).click();
    await page
      .getByRole('alertdialog', { name: 'Smazat skupinu' })
      .getByRole('button', { name: 'Smazat' })
      .click();
    await expect(
      page.getByRole('button', { name: `Smazat skupinu ${GROUP}` }),
    ).toHaveCount(0);

    await page.getByRole('tab', { name: 'Rozvrh' }).click();
    await expect(page.getByTestId('timetable-version-info')).toContainText(
      version,
    );
    await page.locator('p-select[name="classPicker"]').click();
    await page.getByRole('option', { name: '5.A', exact: true }).click();
    // A period without lessons isn't shown at all: the whole grid then.
    await expect(page.locator('tbody td[data-period]').first()).toBeVisible();
    await expect(page.getByText(GROUP)).toHaveCount(0);
    await discard(admin, versionId);
  });
});

// In the version that applies only who's in a group changes, and the family hears
// about it. Jan leaves the seeded group and comes back, as he was.
test('a pupil leaving a group of the valid version tells the family', async ({
  page,
  request,
}) => {
  await openGroups(page);
  await expect(page.getByTestId('groups-members-only')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Nová skupina' })).toHaveCount(
    0,
  );
  await expect(
    page.getByRole('button', { name: `Smazat skupinu ${SEEDED}` }),
  ).toHaveCount(0);

  const setJan = async () => {
    await page
      .getByRole('button', { name: `Upravit skupinu ${SEEDED}` })
      .click();
    const dialog = page.getByRole('dialog', { name: `Žáci skupiny ${SEEDED}` });
    await expect(dialog.getByLabel('Název')).toHaveCount(0);
    await dialog.locator('p-multiselect[name="groupStudents"]').click();
    await page.getByRole('option', { name: /^Novák Jan/ }).click();
    await page.keyboard.press('Escape');
    await dialog.getByRole('button', { name: 'Uložit' }).click();
    await expect(dialog).toBeHidden();
  };
  const url = new URL(page.url());
  const api = `http://${url.hostname}:${Number(url.port || 4200) + 880}/api`;
  const { token } = await (
    await request.post(`${api}/auth/login`, {
      data: { username: 'rodic', password: 'rodic123', schoolSlug: 'skola' },
    })
  ).json();
  const headers = { Authorization: `Bearer ${token}` };
  const notice = async (): Promise<
    { id: number; body: string } | undefined
  > => {
    const { items } = await (
      await request.get(`${api}/notifications`, { headers })
    ).json();
    return items.find(
      (n: { title: string; body: string; read: boolean }) =>
        n.title === 'Změna rozvrhu — Jan' && n.body.includes(SEEDED) && !n.read,
    );
  };
  // A rerun on the same data: what's unread from before would take this in.
  const earlier = await notice();
  if (earlier) {
    await request.post(`${api}/notifications/${earlier.id}/read`, { headers });
  }
  const noticeBody = async () => (await notice())?.body ?? '';

  // Each heard about at the day's end, the second added to the first.
  await setJan();
  expect(await noticeBody()).toBe('');
  await runDayEndJob(page, request);
  await expect.poll(noticeBody).toContain(`Vyřazení ze skupiny ${SEEDED}`);
  await setJan();
  await runDayEndJob(page, request);
  await expect.poll(noticeBody).toContain(`Zařazení do skupiny ${SEEDED}`);
});

// A division of a pupil's choice teaches a row of the plan of its own, a
// placeholder made for it here and deleted with it.
test('a division teaches a row of the učební plán', async ({
  page,
  request,
}) => {
  const row = 'Volitelný předmět e2e';
  const version = `E2E dělení ${Date.now()}`;
  await login(page, 'admin', 'admin123');
  const admin = await versionsApi(page, request);
  const versionId = await draftCopy(admin, version, 3000);
  await openNav(page, 'Administrace');
  await page.getByRole('tab', { name: 'Předměty' }).click();
  await page.getByRole('button', { name: 'Nový předmět' }).click();
  const subjectDialog = page.getByRole('dialog', { name: 'Nový předmět' });
  await subjectDialog.getByLabel('Název předmětu').fill(row);
  await subjectDialog.getByRole('button', { name: 'Vytvořit' }).click();
  await expect(subjectDialog).toBeHidden();

  await page.getByRole('tab', { name: 'Výuka' }).click();
  await pickVersion(page, version);
  await page.getByRole('tab', { name: 'Skupiny' }).click();
  await page.getByRole('button', { name: 'Nové dělení' }).click();
  const dialog = page.getByRole('dialog', { name: 'Nové dělení' });
  await dialog.getByLabel('Název').fill('Volba e2e');
  await dialog.locator('p-multiselect[name="divisionClasses"]').click();
  await page.getByRole('option', { name: '5.A', exact: true }).click();
  await page.keyboard.press('Escape');
  const covers = dialog.getByRole('combobox', { name: 'Řádek učebního plánu' });
  await pick(page, covers, row);
  await dialog.getByRole('spinbutton', { name: 'Hodiny týdně' }).fill('1');
  await dialog.getByRole('button', { name: 'Uložit' }).click();
  await expect(dialog).toBeHidden();
  await expect(
    page.getByRole('region', { name: 'Dělení Volba e2e' }),
  ).toBeInViewport();

  await page.getByRole('button', { name: 'Upravit dělení Volba e2e' }).click();
  const edit = page.getByRole('dialog', { name: 'Upravit dělení' });
  await expect(
    edit.getByRole('combobox', { name: 'Řádek učebního plánu' }),
  ).toHaveText(row);
  await edit.getByRole('button', { name: 'Zrušit' }).click();
  await page.getByRole('button', { name: 'Smazat dělení Volba e2e' }).click();
  await page
    .getByRole('alertdialog', { name: 'Smazat dělení' })
    .getByRole('button', { name: 'Smazat' })
    .click();
  await expect(
    page.getByRole('button', { name: 'Upravit dělení Volba e2e' }),
  ).toHaveCount(0);
  await discard(admin, versionId);

  await page.getByRole('tab', { name: 'Škola' }).click();
  await page.getByRole('tab', { name: 'Předměty' }).click();
  await page
    .getByRole('row', { name: new RegExp(row) })
    .getByRole('button', { name: 'Smazat' })
    .click();
  await expect(page.getByRole('row', { name: new RegExp(row) })).toHaveCount(0);
});

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
    .getByRole('link', { name: 'Kalendář', exact: true })
    .click();
  await expect(page).toHaveURL(/\/skola\/events$/);
  // The week list: new events start now, so they are in it.
  await page.getByRole('button', { name: 'List', exact: true }).click();
}

async function createEvent(
  page: import('@playwright/test').Page,
  title: string,
  audience: string,
) {
  await page.getByRole('button', { name: 'Nová akce' }).click();
  const dialog = page.getByRole('dialog', { name: 'Nová akce' });
  await dialog.getByLabel(/^Název/).fill(title);
  await dialog.locator('#eventAudience').click();
  await page.getByRole('option', { name: audience, exact: true }).click();
  await dialog.locator('button[type="submit"]').click();
  await expect(dialog).toBeHidden();
}

// Unique per run, so the events are found even when earlier runs left
// events behind in a local database.
const runId = Date.now();

// Two events — one for everyone (only those whose events reach the whole
// school make those: the admin), one private by the teacher — checked from
// the parent's side, then removed by whoever made them. Titles are matched with .first(): an
// event running past midnight ("now + 1 h" late in the evening) is listed
// under both days.
test.describe.serial('school events', () => {
  const publicTitle = `E2E výlet ${runId}`;
  const privateTitle = `E2E poznámka ${runId}`;

  test('admin creates a public event, the teacher a private one', async ({
    page,
  }) => {
    await login(page, 'admin', 'admin123');
    await createEvent(page, publicTitle, 'Všichni');
    await expect(page.getByText(publicTitle).first()).toBeVisible();

    await page.context().clearCookies();
    await login(page, 'ucitel', 'ucitel123');
    await createEvent(page, privateTitle, 'Pouze já');
    await expect(page.getByText(privateTitle).first()).toBeVisible();
  });

  test('parent sees only the public one and cannot create events', async ({
    page,
    waitForApiIdle,
  }) => {
    await login(page, 'rodic', 'rodic123');

    await expect(page.getByRole('button', { name: 'Nová akce' })).toHaveCount(
      0,
    );
    await expect(page.getByText(publicTitle).first()).toBeVisible();
    await waitForApiIdle();
    await expect(page.getByText(privateTitle)).toHaveCount(0);

    // A click opens nothing for a parent — events are read-only for them.
    await page.getByText(publicTitle).first().click();
    await expect(page.getByRole('dialog')).toHaveCount(0);
  });

  test('each deletes their own event', async ({ page }) => {
    for (const [user, title] of [
      ['ucitel', privateTitle],
      ['admin', publicTitle],
    ]) {
      await page.context().clearCookies();
      await login(page, user, `${user}123`);
      await page.getByText(title).first().click();
      const dialog = page.getByRole('dialog', { name: 'Upravit akci' });
      await dialog.getByRole('button', { name: 'Smazat' }).click();
      await expect(dialog).toBeHidden();
      await expect(page.getByText(title)).toHaveCount(0);
    }
  });
});

test('recipient filters ignore case and diacritics', async ({ page }) => {
  await login(page, 'admin', 'admin123');
  await page.getByRole('button', { name: 'Nová akce' }).click();
  const dialog = page.getByRole('dialog', { name: 'Nová akce' });
  await dialog.locator('#eventAudience').click();
  await page.getByRole('option', { name: 'Výběr', exact: true }).click();

  await dialog.getByLabel('Hledat žáka').fill('jan novak');
  await expect(dialog.getByText('Jan Novák')).toBeVisible();
});

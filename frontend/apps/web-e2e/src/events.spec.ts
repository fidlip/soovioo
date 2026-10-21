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
  await dialog.getByPlaceholder('Název akce').fill(title);
  await dialog.locator('#eventAudience').click();
  await page.getByRole('option', { name: audience, exact: true }).click();
  await dialog.locator('button[type="submit"]').click();
  await expect(dialog).toBeHidden();
}

// Unique per run, so the events are found even when earlier runs left
// events behind in a local database.
const runId = Date.now();

// Two events by the teacher — one for everyone, one private — checked from
// the parent's side, then removed. Titles are matched with .first(): an
// event running past midnight ("now + 1 h" late in the evening) is listed
// under both days.
test.describe.serial('school events', () => {
  const publicTitle = `E2E výlet ${runId}`;
  const privateTitle = `E2E poznámka ${runId}`;

  test('teacher creates a public and a private event', async ({ page }) => {
    await login(page, 'ucitel', 'ucitel123');

    await createEvent(page, publicTitle, 'Všichni');
    await createEvent(page, privateTitle, 'Pouze já');

    await expect(page.getByText(publicTitle).first()).toBeVisible();
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

  test('teacher deletes both events', async ({ page }) => {
    await login(page, 'ucitel', 'ucitel123');

    for (const title of [publicTitle, privateTitle]) {
      await page.getByText(title).first().click();
      const dialog = page.getByRole('dialog', { name: 'Upravit akci' });
      await dialog.getByRole('button', { name: 'Smazat' }).click();
      await expect(dialog).toBeHidden();
      await expect(page.getByText(title)).toHaveCount(0);
    }
  });
});

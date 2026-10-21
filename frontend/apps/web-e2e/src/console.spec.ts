import { expect, test } from './fixtures';

/**
 * Every page, in every role, must load without console errors/warnings —
 * the check itself is the auto `consoleGuard` fixture in fixtures.ts; this
 * spec just makes sure every page actually gets visited.
 */

const SCHOOL_PAGES = [
  'home',
  'grades',
  'absences',
  'timetable',
  'events',
  'messages',
  'homework',
  'behavior',
  'payments',
  'admin',
  'settings',
];

for (const username of ['admin', 'ucitel', 'rodic', 'zak']) {
  test(`${username}: every page loads with a clean console`, async ({
    page,
    waitForApiIdle,
  }) => {
    // A full page load per agenda — on a CI runner against the dev server
    // that's more than the default 30 s for the whole loop.
    test.slow();
    await page.goto('/skola/login');
    await page.locator('#username').fill(username);
    await page.locator('#password').fill(`${username}123`);
    await page.getByRole('button', { name: 'Přihlásit se' }).click();
    await expect(page).toHaveURL(/\/skola\/home$/);

    for (const path of SCHOOL_PAGES) {
      await page.goto(`/skola/${path}`);
      await expect(page.locator('main h1')).toBeVisible();
      // Let the page's data requests settle before moving on.
      await waitForApiIdle();
    }
  });
}

test('superadmin: every page loads with a clean console', async ({
  page,
  waitForApiIdle,
}) => {
  test.slow();
  await page.goto('/superadmin/login');
  await page.locator('#username').fill('superadmin');
  await page.locator('#password').fill('superadmin123');
  await page.getByRole('button', { name: 'Přihlásit se' }).click();
  await expect(page).toHaveURL(/\/superadmin$/);

  for (const path of ['superadmin', 'superadmin/logs', 'superadmin/settings']) {
    await page.goto(`/${path}`);
    await expect(page.locator('main h1')).toBeVisible();
    await waitForApiIdle();
  }
});

test('logged-out pages load with a clean console', async ({
  page,
  waitForApiIdle,
}) => {
  for (const path of [
    '/skola/login',
    '/superadmin/login',
    '/skola/accept-invite?token=x',
  ]) {
    await page.goto(path);
    await expect(page.getByRole('button').first()).toBeVisible();
    await waitForApiIdle();
  }
});

import { expect, test } from './fixtures';

/**
 * Covers the slug-routing + auth-guard redirect chain and basic login/logout
 * for the demo school, against the real backend (see ROADMAP.md — "Poznámky
 * k prostředí": both dev servers must be running on their usual ports).
 */

test('unauthenticated visit redirects through the demo slug to login', async ({
  page,
}) => {
  await page.goto('/');
  await expect(page).toHaveURL(/\/skola\/login$/);
});

test('unauthenticated deep link redirects to login instead of the page', async ({
  page,
}) => {
  await page.goto('/skola/grades');
  await expect(page).toHaveURL(/\/skola\/login\?returnUrl=%2Fskola%2Fgrades$/);
});

test.describe('unknown school', () => {
  // The browser logs the school lookup's 404 as a console error.
  test.use({ allowedConsoleMessages: [/status of 404/] });

  test('an unknown school slug shows "škola neexistuje" instead of a login form, under the same address', async ({
    page,
  }) => {
    await page.goto('/neexistujici-skola/login');
    await expect(
      page.getByRole('heading', { name: 'Škola neexistuje' }),
    ).toBeVisible();
    await expect(page.getByText('neexistujici-skola')).toBeVisible();
    // The typed address stays, so a typo can be fixed in place.
    await expect(page).toHaveURL(/\/neexistujici-skola\/login$/);
  });
});

test.describe('wrong credentials', () => {
  // The browser itself logs the rejected login's 401 as a console error.
  test.use({ allowedConsoleMessages: [/status of 401/] });

  test('wrong credentials show an error and stay on the login page', async ({
    page,
  }) => {
    await page.goto('/skola/login');
    await page.locator('#username').fill('admin');
    await page.locator('#password').fill('not-the-right-password');
    await page.getByRole('button', { name: 'Přihlásit se' }).click();

    await expect(page.getByText('Neplatné přihlašovací údaje.')).toBeVisible();
    await expect(page).toHaveURL(/\/skola\/login$/);
  });
});

test('teacher can log in, see the nav, and log out', async ({ page }) => {
  await page.goto('/skola/login');
  await page.locator('#username').fill('ucitel');
  await page.locator('#password').fill('ucitel123');
  await page.getByRole('button', { name: 'Přihlásit se' }).click();

  await expect(page).toHaveURL(/\/skola\/home$/);
  await expect(
    page.getByRole('link', { name: 'Známky', exact: true }),
  ).toBeVisible();
  // The demo "ucitel" account is the class teacher (ClassTeacherId) of 5.A,
  // so it gets the Administrace link too — a restricted view (own class's
  // students + parent links only), see admin-page.ts AccessLevel.
  await expect(
    page.getByRole('link', { name: 'Administrace', exact: true }),
  ).toBeVisible();

  await page.getByRole('button', { name: 'Odhlásit se' }).click();
  await expect(page).toHaveURL(/\/skola\/login$/);
});

test('admin sees the administration nav link', async ({ page }) => {
  await page.goto('/skola/login');
  await page.locator('#username').fill('admin');
  await page.locator('#password').fill('admin123');
  await page.getByRole('button', { name: 'Přihlásit se' }).click();

  await expect(page).toHaveURL(/\/skola\/home$/);
  await expect(
    page.getByRole('link', { name: 'Administrace', exact: true }),
  ).toBeVisible();
});

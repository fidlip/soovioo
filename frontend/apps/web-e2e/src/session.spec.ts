import { expect, test } from './fixtures';

/**
 * httpOnly refresh-cookie sessions: the access token lives in memory only,
 * so every reload goes through the silent refresh at app startup.
 */

async function login(
  page: import('@playwright/test').Page,
  rememberMe: boolean,
) {
  await page.goto('/demo/login');
  await page.locator('#username').fill('admin');
  await page.locator('#password').fill('admin123');
  if (rememberMe) {
    await page.getByLabel('Zůstat přihlášen').check();
  }
  await page.getByRole('button', { name: 'Přihlásit se' }).click();
  await expect(page).toHaveURL(/\/demo\/home$/);
}

const refreshCookie = async (page: import('@playwright/test').Page) =>
  (await page.context().cookies()).find((c) => c.name === 'soovioo_refresh');

test('a reload keeps the user signed in without any token in storage', async ({
  page,
}) => {
  await login(page, false);

  await page.reload();

  await expect(page).toHaveURL(/\/demo\/home$/);
  await expect(page.getByRole('link', { name: 'Známky' })).toBeVisible();
  const stored = await page.evaluate(() => JSON.stringify(localStorage));
  expect(stored).not.toMatch(/eyJ/); // no JWT anywhere in localStorage
  const cookie = await refreshCookie(page);
  expect(cookie?.httpOnly).toBe(true);
});

test('without "Zůstat přihlášen" the refresh cookie ends with the browser', async ({
  page,
}) => {
  await login(page, false);

  expect((await refreshCookie(page))?.expires).toBe(-1);
});

test('"Zůstat přihlášen" sets a persistent refresh cookie', async ({
  page,
}) => {
  await login(page, true);

  const cookie = await refreshCookie(page);
  const days = ((cookie?.expires as number) * 1000 - Date.now()) / 86_400_000;
  expect(days).toBeGreaterThan(29);
});

test('after logout a reload stays logged out', async ({ page }) => {
  await login(page, false);
  await page.getByRole('button', { name: 'admin' }).click();
  await page.getByText('Odhlásit se').click();
  await expect(page).toHaveURL(/\/demo\/login$/);

  await page.goto('/demo/home');

  await expect(page).toHaveURL(/\/demo\/login$/);
});

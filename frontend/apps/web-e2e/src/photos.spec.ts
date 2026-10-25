import { Page } from '@playwright/test';
import { expect, test } from './fixtures';

/** 2×2 red PNG — the server re-encodes whatever it gets anyway. */
const PNG = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAEElEQVR4nGP4z8AARAwQCgAf7gP9i18U1AAAAABJRU5ErkJggg==',
  'base64',
);

/** The dev API runs 880 ports above the web dev server (see devApiPort). */
function apiUrl(page: Page): string {
  const url = new URL(page.url());
  return `http://${url.hostname}:${Number(url.port || 4200) + 880}/api`;
}

test('a teacher uploads and deletes their own profile photo', async ({
  page,
  request,
}) => {
  // A teacher of its own: the seeded one's avatar is on the pages other
  // tests check for a clean console, deleting it there would 404.
  await page.goto('/skola/login');
  const api = apiUrl(page);
  const { token } = await (
    await request.post(`${api}/auth/login`, {
      data: { username: 'admin', password: 'admin123', schoolSlug: 'skola' },
    })
  ).json();
  const username = `foto${Date.now()}`;
  expect(
    (
      await request.post(`${api}/users`, {
        headers: { Authorization: `Bearer ${token}` },
        data: {
          username,
          password: 'foto12345',
          roles: ['Teacher'],
          firstName: 'Fotka',
          lastName: 'Testovací',
        },
      })
    ).ok(),
  ).toBe(true);

  await page.locator('#username').fill(username);
  await page.locator('#password').fill('foto12345');
  await page.getByRole('button', { name: 'Přihlásit se' }).click();
  // A password set by the admin sends them to Nastavení to change it.
  await expect(page).toHaveURL(/\/skola\/settings$/);

  const card = page.locator('p-card').filter({ hasText: 'Profilová fotka' });
  await card.locator('input[type=file]').setInputFiles({
    name: 'fotka.png',
    mimeType: 'image/png',
    buffer: PNG,
  });
  await expect(page.getByText('Fotka nahrána.')).toBeVisible();
  await expect(card.locator('app-avatar img')).toBeVisible();

  await card.getByRole('button', { name: 'Smazat fotku' }).click();
  await expect(page.getByText('Fotka smazána.')).toBeVisible();
  await expect(card.locator('app-avatar img')).toHaveCount(0);
});

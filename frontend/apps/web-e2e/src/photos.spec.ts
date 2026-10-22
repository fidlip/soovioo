import { expect, test } from './fixtures';

/** 2×2 red PNG — the server re-encodes whatever it gets anyway. */
const PNG = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAEElEQVR4nGP4z8AARAwQCgAf7gP9i18U1AAAAABJRU5ErkJggg==',
  'base64',
);

test('a teacher uploads and deletes their own profile photo', async ({
  page,
}) => {
  await page.goto('/skola/login');
  await page.locator('#username').fill('ucitel');
  await page.locator('#password').fill('ucitel123');
  await page.getByRole('button', { name: 'Přihlásit se' }).click();
  await expect(page).toHaveURL(/\/skola\/home$/);
  await page.goto('/skola/settings');

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

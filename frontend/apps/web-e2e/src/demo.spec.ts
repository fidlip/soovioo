import { expect, test } from './fixtures';

/**
 * "Vyzkoušet": entry page → wizard → generated school, logged in as the
 * chosen role → switching users → deleting the demo. Creates a real demo
 * school (a few seconds of generation) and removes it again.
 */
test('demo: wizard, user picker, delete', async ({ page }) => {
  test.slow();
  await page.goto('/demo?new=1');

  await page.getByRole('button', { name: /Střední škola/ }).click();
  await expect(page.getByLabel('Název školy')).toHaveValue('Gymnázium Ukázkov');
  await page.getByRole('radio', { name: /^1/ }).click();
  await page.getByRole('button', { name: 'Další' }).click();
  // Contact step — e-mail is optional.
  await page.getByRole('button', { name: 'Další' }).click();
  await page.getByRole('radio', { name: /Rodič/ }).click();
  await page.getByRole('button', { name: 'Vytvořit ukázkovou školu' }).click();

  await expect(page).toHaveURL(/\/demo-[a-z0-9]{11}\/home$/, {
    timeout: 30_000,
  });
  await expect(page.getByText('Ukázková škola', { exact: true })).toBeVisible();
  const slug = new URL(page.url()).pathname.split('/')[1];

  // Back to the entry page: a returning visitor goes straight to the demo.
  await page.goto('/demo');
  await expect(page).toHaveURL(new RegExp(`/${slug}/login$`));
  await expect(
    page.getByRole('heading', { name: 'Za koho se chcete podívat?' }),
  ).toBeVisible();

  // Switch to the admin without any password.
  await page
    .getByRole('button', { name: /^Vedení školy/ })
    .first()
    .click();
  await expect(page).toHaveURL(new RegExp(`/${slug}/home$`));
  await expect(page.getByRole('link', { name: 'Logy' })).toBeVisible();

  await page.getByRole('button', { name: 'Možnosti dema' }).click();
  await page.getByRole('menuitem', { name: 'Smazat demo' }).click();
  await page.getByRole('button', { name: 'Smazat', exact: true }).click();
  await expect(page).toHaveURL(/\/demo\?new=1$/);
  await expect(
    page.getByRole('heading', { name: 'Vyzkoušejte Soovioo' }),
  ).toBeVisible();
});

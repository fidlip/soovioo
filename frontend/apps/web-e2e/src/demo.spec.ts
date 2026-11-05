import { expect, test } from './fixtures';

/**
 * "Vyzkoušet": entry page → wizard → generated school, logged in as the
 * chosen role → switching users → deleting the demo. Creates a real demo
 * school (a few seconds of generation) and removes it again.
 */
test('demo: wizard, user picker, delete', async ({ page }) => {
  test.slow();
  await page.goto('/demo?new=1');

  // Picking an option moves on; the role creates the demo.
  await page.getByRole('radio', { name: /Střední škola/ }).click();
  await expect(page.getByLabel('Název školy')).toHaveValue('Gymnázium Ukázkov');
  await page.getByRole('radio', { name: /^1/ }).click();
  // Contact step — e-mail is optional.
  await page.getByRole('button', { name: 'Další' }).click();
  await page.getByRole('radio', { name: /Rodič/ }).click();

  await expect(page).toHaveURL(/\/demo-[a-z0-9]{11}\/home$/, {
    timeout: 30_000,
  });
  await expect(page.getByText('Ukázková škola', { exact: true })).toBeVisible();
  const slug = new URL(page.url()).pathname.split('/')[1];
  // A second tab of the demo, told about the switch below.
  const other = await page.context().newPage();
  await other.goto(`/${slug}/home`);
  await expect(
    other.getByText('Ukázková škola', { exact: true }),
  ).toBeVisible();

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
  await expect(
    other.getByRole('alert').filter({ hasText: 'V jiné záložce' }),
  ).toContainText('V jiné záložce jste se přihlásili jako');
  await other.close();

  // Empty school: everything goes but the admin account (and the log).
  await page.getByRole('button', { name: 'Možnosti dema' }).click();
  await page.getByRole('menuitem', { name: /Vymazat data/ }).click();
  // Already on Home — wait for the reload that follows the re-login.
  const reloaded = page.waitForEvent('load');
  await page.getByRole('button', { name: 'Vymazat data', exact: true }).click();
  await reloaded;
  await expect(page).toHaveURL(new RegExp(`/${slug}/home$`));
  await page.goto(`/${slug}/admin`);
  // Tabs render lazily — open Osoby: only the demo admin is left.
  await page.getByRole('tab', { name: 'Osoby' }).click();
  await expect(
    page.getByRole('tabpanel', { name: 'Osoby' }).locator('tbody tr'),
  ).toHaveCount(1);

  await page.getByRole('button', { name: 'Možnosti dema' }).click();
  await page.getByRole('menuitem', { name: 'Smazat celé demo' }).click();
  await page
    .getByRole('alertdialog')
    .getByRole('button', { name: 'Smazat', exact: true })
    .click();
  await expect(page).toHaveURL(/\/demo\?new=1$/);
  await expect(
    page.getByRole('heading', { name: 'Vyzkoušejte Soovioo' }),
  ).toBeVisible();
});

test('demo: wizard by keyboard, nothing picked in advance', async ({
  page,
}) => {
  await page.goto('/demo?new=1');
  const types = page.getByRole('radiogroup', { name: 'Typ školy' });
  await expect(types.getByRole('radio')).toHaveCount(2);
  await expect(types.getByRole('radio', { checked: true })).toHaveCount(0);

  // Arrows pick, Enter confirms and moves on.
  await page.keyboard.press('ArrowRight');
  await expect(
    types.getByRole('radio', { name: /Základní škola/ }),
  ).toBeChecked();
  await page.keyboard.press('ArrowRight');
  await expect(
    types.getByRole('radio', { name: /Střední škola/ }),
  ).toBeChecked();
  await page.keyboard.press('Enter');
  await expect(page.getByLabel('Název školy')).toHaveValue('Gymnázium Ukázkov');
  const sizes = page.getByRole('radiogroup', { name: 'Počet tříd v ročníku' });
  await expect(sizes.getByRole('radio')).toHaveCount(3);
  await expect(sizes.getByRole('radio', { checked: true })).toHaveCount(0);

  // Backspace goes back, the earlier pick stays.
  await page.keyboard.press('Backspace');
  await expect(
    types.getByRole('radio', { name: /Střední škola/ }),
  ).toBeChecked();
  await expect(
    types.getByRole('radio', { name: /Střední škola/ }),
  ).toBeFocused();
  await page.keyboard.press('Enter');

  // Backspace in the name field edits the name, Enter hands over to the sizes.
  await page.getByLabel('Název školy').fill('Gymnázium Testov');
  await page.getByLabel('Název školy').press('Backspace');
  await expect(page.getByLabel('Název školy')).toHaveValue('Gymnázium Testo');
  await page.getByLabel('Název školy').press('Enter');
  await page.keyboard.press('ArrowRight');
  await page.keyboard.press('ArrowRight');
  await expect(sizes.getByRole('radio', { name: /^3/ })).toBeChecked();
  await page.keyboard.press('Enter');

  await expect(page.getByLabel('E-mail')).toBeVisible();
  await page.getByLabel('E-mail').press('Enter');
  const roles = page.getByRole('radiogroup', { name: 'Role' });
  await expect(roles.getByRole('radio')).toHaveCount(4);
  await expect(roles.getByRole('radio', { checked: true })).toHaveCount(0);
  await page.keyboard.press('ArrowDown');
  await expect(
    roles.getByRole('radio', { name: /Vedení školy/ }),
  ).toBeChecked();

  await page.keyboard.press('Backspace');
  await page.keyboard.press('Backspace');
  await expect(sizes.getByRole('radio', { name: /^3/ })).toBeChecked();
  await expect(page.getByLabel('Název školy')).toHaveValue('Gymnázium Testo');
});

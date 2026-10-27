import { Locator, Page } from '@playwright/test';
import { expect, test } from './fixtures';

// Osoby and Uživatelé list everyone's photo — the photos test deletes its
// own teacher's in parallel, a thumbnail already asked for then 404s.
test.use({ allowedConsoleMessages: [/status of 404/] });

/** Picks an option of a multiselect and closes its list — Escape only
 * once the list is open, otherwise it closes the dialog instead. */
async function pick(page: Page, field: Locator, option: string | RegExp) {
  await field.click();
  const list = page.getByRole('listbox');
  await expect(list).toBeVisible();
  await list
    .getByRole('option', { name: option, exact: typeof option === 'string' })
    .click();
  // On a busy runner the first Escape can come before the list listens.
  await expect(async () => {
    await page.keyboard.press('Escape');
    await expect(list).toBeHidden({ timeout: 1_000 });
  }).toPass();
}

test('an account is a person’s: picked, completed with the missing profile, then created', async ({
  page,
}) => {
  // Four dialogs across two tabs — over 30 s on a busy CI runner.
  test.slow();
  const lastName = `Osoba${Date.now()}`;
  await page.goto('/skola/login');
  await page.locator('#username').fill('admin');
  await page.locator('#password').fill('admin123');
  await page.getByRole('button', { name: 'Přihlásit se' }).click();
  await expect(page).toHaveURL(/\/skola\/home$/);
  await page.getByRole('link', { name: 'Administrace', exact: true }).click();

  // A teacher without an account, in Osoby.
  await page.getByRole('tab', { name: 'Osoby' }).click();
  const people = page.getByRole('tabpanel', { name: 'Osoby' });
  await people.getByRole('button', { name: 'Nová osoba' }).click();
  const personDialog = page.getByRole('dialog', { name: 'Nová osoba' });
  await personDialog.getByRole('textbox', { name: /^Jméno/ }).fill('Eva');
  await personDialog.getByRole('textbox', { name: /^Příjmení/ }).fill(lastName);
  await pick(
    page,
    personDialog.locator('p-multiselect[name="profiles"]'),
    'Učitel',
  );
  await personDialog.getByRole('textbox', { name: 'Aprobace' }).fill('M');
  await personDialog.getByRole('button', { name: 'Vytvořit osobu' }).click();
  await expect(personDialog).toBeHidden();
  const personRow = people.locator('tr', { hasText: lastName });
  await expect(personRow.getByText('Učitel (M)')).toBeVisible();
  await expect(personRow.getByText('bez účtu')).toBeVisible();

  // Her account as a teacher — she's offered — then also a parent, which
  // she has no profile for yet.
  await page.getByRole('tab', { name: 'Uživatelé' }).click();
  await page.getByRole('button', { name: 'Nový uživatel' }).click();
  const userDialog = page.getByRole('dialog', { name: 'Nový uživatel' });
  await pick(page, userDialog.locator('p-multiselect[name="roles"]'), 'Učitel');
  await userDialog.locator('#userPersonId').click();
  await page.getByRole('option', { name: `Eva ${lastName}` }).click();
  await pick(page, userDialog.locator('p-multiselect[name="roles"]'), 'Rodič');
  await expect(
    userDialog.getByText('Role Rodič potřebuje profil rodiče'),
  ).toBeVisible();
  await expect(
    userDialog.getByRole('button', { name: 'Vytvořit uživatele' }),
  ).toBeDisabled();

  await userDialog.getByRole('button', { name: 'Upravit osobu' }).click();
  const editDialog = page.getByRole('dialog', { name: 'Upravit osobu' });
  await pick(
    page,
    editDialog.locator('p-multiselect[name="profiles"]'),
    'Rodič',
  );
  // A child too — deleting her below takes the link along.
  await pick(
    page,
    editDialog.locator('p-multiselect[name="childIds"]'),
    /^Jan Novák/,
  );
  await editDialog.getByRole('button', { name: 'Uložit' }).click();
  await expect(editDialog).toBeHidden();
  await expect(
    userDialog.getByText('Role Rodič potřebuje profil rodiče'),
  ).toHaveCount(0);
  await expect(
    userDialog.getByRole('group', { name: `Osoba Eva ${lastName}` }),
  ).toContainText('Rodič:');

  await userDialog.locator('#password').fill('heslo123');
  await userDialog.getByRole('button', { name: 'Vytvořit uživatele' }).click();
  await expect(userDialog).toBeHidden();
  const userRow = page
    .getByRole('tabpanel', { name: 'Uživatelé' })
    .locator('tr', { hasText: lastName });
  await expect(userRow).toHaveCount(1);

  // Deleting the account leaves the person; then she goes too.
  await userRow.getByRole('button', { name: 'Smazat' }).click();
  await page
    .getByRole('alertdialog')
    .getByRole('button', { name: 'Smazat' })
    .click();
  await expect(userRow).toHaveCount(0);
  await page.getByRole('tab', { name: 'Osoby' }).click();
  await expect(personRow.getByText('bez účtu')).toBeVisible();
  await personRow.getByRole('button', { name: 'Smazat' }).click();
  await page
    .getByRole('alertdialog')
    .getByRole('button', { name: 'Smazat' })
    .click();
  await expect(personRow).toHaveCount(0);
});

test('what is typed stays when the dialog’s lists arrive late', async ({
  page,
  waitForApiIdle,
}) => {
  let release: () => void = () => undefined;
  const held = new Promise<void>((resolve) => (release = resolve));
  await page.route('**/api/countries', async (route) => {
    await held;
    await route.continue();
  });
  await page.goto('/skola/login');
  await page.locator('#username').fill('admin');
  await page.locator('#password').fill('admin123');
  await page.getByRole('button', { name: 'Přihlásit se' }).click();
  await expect(page).toHaveURL(/\/skola\/home$/);
  await page.getByRole('link', { name: 'Administrace', exact: true }).click();
  await page.getByRole('tab', { name: 'Osoby' }).click();
  const requested = page.waitForRequest('**/api/countries');
  await page.getByRole('button', { name: 'Nová osoba' }).click();
  await requested;
  const dialog = page.getByRole('dialog', { name: 'Nová osoba' });
  const lastName = dialog.getByRole('textbox', { name: /^Příjmení/ });
  await lastName.fill('Pozdní');

  release();
  await waitForApiIdle();
  await expect(lastName).toHaveValue('Pozdní');
});

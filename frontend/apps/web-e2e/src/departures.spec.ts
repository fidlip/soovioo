import { expect, test } from './fixtures';

/** dd.mm.yyyy of yesterday — the DatePicker's input format. */
function yesterday(): string {
  const date = new Date();
  date.setDate(date.getDate() - 1);
  const pad = (n: number) => `${n}`.padStart(2, '0');
  return `${pad(date.getDate())}.${pad(date.getMonth() + 1)}.${date.getFullYear()}`;
}

test('a pupil who left moves to Odchody and can be erased', async ({
  page,
}) => {
  // A pupil of its own — the seeded one is used by other tests.
  const lastName = `Odchod${Date.now()}`;

  await page.goto('/skola/login');
  await page.locator('#username').fill('admin');
  await page.locator('#password').fill('admin123');
  await page.getByRole('button', { name: 'Přihlásit se' }).click();
  await expect(page).toHaveURL(/\/skola\/home$/);
  await page.getByRole('link', { name: 'Administrace', exact: true }).click();
  await page.getByRole('tab', { name: 'Žáci' }).click();

  await page.getByRole('button', { name: 'Nový žák' }).click();
  const newDialog = page.getByRole('dialog', { name: 'Nový žák' });
  await newDialog.getByRole('textbox', { name: /^Jméno/ }).fill('Ota');
  await newDialog.getByRole('textbox', { name: /^Příjmení/ }).fill(lastName);
  await newDialog.getByRole('button', { name: 'Přidat žáka' }).click();
  await expect(newDialog).toBeHidden();

  const studentsPanel = page.getByRole('tabpanel', { name: 'Žáci' });
  const row = studentsPanel.locator('tr', { hasText: lastName });
  await row.getByRole('button', { name: 'Odchod ze školy' }).click();
  const leaveDialog = page.getByRole('dialog', {
    name: `Odchod ze školy — Ota ${lastName}`,
  });
  // Typed key by key — the DatePicker ignores a pasted value.
  await leaveDialog
    .getByLabel('Poslední den ve škole')
    .pressSequentially(yesterday());
  await leaveDialog.getByLabel('Poznámka').fill('Přestup');
  await leaveDialog.getByRole('button', { name: 'Uložit odchod' }).click();
  await expect(leaveDialog).toBeHidden();
  await expect(row).toHaveCount(0);

  await page.getByRole('tab', { name: 'Odchody' }).click();
  const formerRow = page
    .getByRole('tabpanel', { name: 'Odchody' })
    .locator('tr', { hasText: lastName });
  await expect(formerRow.getByText('Bývalý')).toBeVisible();

  await formerRow.getByRole('button', { name: 'Vymazat vše hned' }).click();
  await page
    .getByRole('alertdialog')
    .getByRole('button', { name: 'Vymazat' })
    .click();
  await expect(formerRow).toHaveCount(0);
});

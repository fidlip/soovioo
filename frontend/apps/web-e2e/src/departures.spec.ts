import { expect, test } from './fixtures';

/** dd.mm.yyyy `days` from today — the DatePicker's input format. */
function inDays(days: number): string {
  const date = new Date();
  date.setDate(date.getDate() + days);
  const pad = (n: number) => `${n}`.padStart(2, '0');
  return `${pad(date.getDate())}.${pad(date.getMonth() + 1)}.${date.getFullYear()}`;
}

test('a former pupil moves to the archive and can be erased', async ({
  page,
}) => {
  // A pupil of its own — the seeded one is used by other tests.
  const lastName = `Archiv${Date.now()}`;

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
  await row.getByRole('button', { name: 'Ukončení vzdělávání' }).click();
  const leaveDialog = page.getByRole('dialog', {
    name: `Ukončení vzdělávání — Ota ${lastName}`,
  });
  const lastDay = leaveDialog.getByLabel('Poslední den ve škole');
  // Typed key by key — the DatePicker ignores a pasted value.
  await lastDay.pressSequentially(inDays(30));
  await leaveDialog.getByLabel('Poznámka').fill('Přestup');
  await leaveDialog.getByRole('button', { name: 'Uložit' }).click();
  await expect(leaveDialog).toBeHidden();
  await expect(row.getByText(/Končí k/)).toBeVisible();

  // Opened again, the dialog shows what was saved.
  await row.getByRole('button', { name: 'Ukončení vzdělávání' }).click();
  await expect(leaveDialog.getByLabel('Poznámka')).toHaveValue('Přestup');
  await lastDay.press('Control+a');
  await lastDay.pressSequentially(inDays(-1));
  await leaveDialog.getByRole('button', { name: 'Uložit' }).click();
  await expect(leaveDialog).toBeHidden();
  await expect(row).toHaveCount(0);

  await page.getByRole('tab', { name: 'Archiv a výmaz údajů' }).click();
  const formerRow = page
    .getByRole('tabpanel', { name: 'Archiv a výmaz údajů' })
    .locator('tr', { hasText: lastName });
  await expect(formerRow.getByText('Bývalý')).toBeVisible();

  await formerRow.getByRole('button', { name: 'Vymazat vše hned' }).click();
  await page
    .getByRole('alertdialog')
    .getByRole('button', { name: 'Vymazat' })
    .click();
  await expect(formerRow).toHaveCount(0);
});

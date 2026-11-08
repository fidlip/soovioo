import { expect, test } from './fixtures';

async function login(
  page: import('@playwright/test').Page,
  username: string,
  password: string,
) {
  await page.goto('/skola/login');
  await page.locator('#username').fill(username);
  await page.locator('#password').fill(password);
  await page.getByRole('button', { name: 'Přihlásit se' }).click();
  await expect(page).toHaveURL(/\/skola\/home$/);
  await page
    .getByRole('navigation', { name: 'Hlavní navigace' })
    .getByRole('link', { name: 'Platby', exact: true })
    .click();
  await expect(page).toHaveURL(/\/skola\/payments$/);
}

// One payment goes through its life: the teacher requests it from Jan
// Novák's family, the parent sees it unpaid, the teacher marks it paid,
// the parent sees it paid, the teacher removes it. Buttons are matched
// exactly: a payment's header and each recipient row are themselves
// role="button"s whose accessible names include their buttons' labels.
test.describe.serial('payment for one student', () => {
  // Unique per run, so the payment is found even when earlier runs left
  // payments behind in a local database.
  const title = `E2E výlet ${Date.now()}`;
  const card = (page: import('@playwright/test').Page) =>
    page.locator('p-card').filter({ hasText: title });

  test('teacher requests a payment from one student', async ({ page }) => {
    await login(page, 'ucitel', 'ucitel123');

    await page.getByRole('button', { name: 'Nová platba' }).click();
    const dialog = page.getByRole('dialog', { name: 'Nová platba' });
    // The dialog focuses its first field once its open animation ends; on a
    // slow runner that came mid-typing and took the amount's last key.
    const titleInput = dialog.getByLabel(/^Název/);
    await expect(titleInput).toBeFocused();
    await titleInput.fill(title);
    await dialog.locator('#amount').pressSequentially('150');
    await dialog.locator('p-select[name="audience"]').click();
    await page.getByRole('option', { name: 'Výběr', exact: true }).click();
    await dialog.locator('p-multiselect[name="students"]').click();
    await page.getByRole('searchbox').fill('NOVAK');
    await page.getByRole('option', { name: /^Jan Novák/ }).click();
    await page.keyboard.press('Escape');
    await dialog.locator('button[type="submit"]').click();
    await expect(dialog).toBeHidden();

    await expect(card(page)).toHaveCount(1);
    await expect(card(page)).toContainText('Nezaplaceno');
    await expect(card(page)).toContainText('150');
  });

  test('parent sees it unpaid and cannot change it', async ({ page }) => {
    await login(page, 'rodic', 'rodic123');

    await expect(page.getByRole('button', { name: 'Nová platba' })).toHaveCount(
      0,
    );
    await expect(card(page)).toContainText('Nezaplaceno');
    await expect(
      card(page).getByRole('button', { name: 'Smazat platbu', exact: true }),
    ).toHaveCount(0);

    await card(page).getByText(title).click();
    await expect(card(page)).toContainText('Jan Novák');
    await expect(
      card(page).getByRole('button', {
        name: 'Označit jako zaplaceno',
        exact: true,
      }),
    ).toHaveCount(0);
  });

  test('teacher marks it paid', async ({ page }) => {
    await login(page, 'ucitel', 'ucitel123');

    await card(page).getByText(title).click();
    await card(page)
      .getByRole('button', {
        name: 'Označit jako zaplaceno',
        exact: true,
      })
      .click();
    await expect(
      card(page).getByRole('button', { name: 'Zrušit zaplacení', exact: true }),
    ).toBeVisible();
  });

  test('parent sees it paid', async ({ page }) => {
    await login(page, 'rodic', 'rodic123');

    await expect(card(page)).toContainText('Zaplaceno');
    await expect(card(page)).not.toContainText('Nezaplaceno');
  });

  test('teacher deletes the payment', async ({ page }) => {
    await login(page, 'ucitel', 'ucitel123');

    await card(page)
      .getByRole('button', { name: 'Smazat platbu', exact: true })
      .click();
    await expect(card(page)).toHaveCount(0);
  });
});

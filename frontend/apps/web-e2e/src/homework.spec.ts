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
  await page.getByRole('link', { name: 'Domácí úkoly', exact: true }).click();
  await expect(page).toHaveURL(/\/skola\/homework$/);
}

// One homework item goes through its life: the teacher assigns it, the
// parent confirms it done, the teacher sees that and removes it.
test.describe.serial('homework with completion', () => {
  // Unique per run, so the card is found even when earlier runs left
  // homework behind in a local database.
  const text = `E2E úkol ${Date.now()}: pracovní sešit str. 12`;
  const card = (page: import('@playwright/test').Page) =>
    page.locator('p-card').filter({ hasText: text });

  test('teacher assigns homework that requires completion', async ({
    page,
  }) => {
    await login(page, 'ucitel', 'ucitel123');

    await page.getByRole('button', { name: 'Nový úkol' }).click();
    const dialog = page.getByRole('dialog', { name: 'Nový úkol' });
    // The class/subject the teacher teaches is preselected; the due date
    // defaults to today, which still counts as current.
    await dialog.getByLabel(/^Zadání/).fill(text);
    await dialog.getByText('Vyžadovat potvrzení splnění').click();
    await dialog.locator('button[type="submit"]').click();
    await expect(dialog).toBeHidden();

    await expect(card(page)).toHaveCount(1);
    await expect(card(page)).toContainText('Jan Novák — nesplněno');
  });

  test('student sees it but cannot change it', async ({ page }) => {
    await login(page, 'zak', 'zak123');

    await expect(card(page)).toHaveCount(1);
    await expect(page.getByRole('button', { name: 'Nový úkol' })).toHaveCount(
      0,
    );
    await expect(
      card(page).getByRole('button', { name: 'Upravit úkol' }),
    ).toHaveCount(0);
  });

  test('parent confirms it done', async ({ page }) => {
    await login(page, 'rodic', 'rodic123');

    await expect(
      card(page).getByRole('button', { name: 'Smazat úkol' }),
    ).toHaveCount(0);
    await card(page).getByText('Jan Novák — nesplněno').click();
    await expect(card(page)).toContainText('Jan Novák — splněno');
  });

  test('teacher sees it done and deletes the homework', async ({ page }) => {
    await login(page, 'ucitel', 'ucitel123');

    await expect(card(page)).toContainText('Jan Novák — splněno');
    await card(page).getByRole('button', { name: 'Smazat úkol' }).click();
    await expect(card(page)).toHaveCount(0);
  });
});

test('a dialog opened before the teacher’s timetable arrives offers it then', async ({
  page,
}) => {
  let release: () => void = () => undefined;
  const held = new Promise<void>((resolve) => (release = resolve));
  await page.route('**/api/teachers/*/timetable', async (route) => {
    await held;
    await route.continue();
  });
  await login(page, 'ucitel', 'ucitel123');
  await page.getByRole('button', { name: 'Nový úkol' }).click();
  const dialog = page.getByRole('dialog', { name: 'Nový úkol' });
  await dialog.getByLabel(/^Zadání/).fill('Pozdní rozvrh');
  const submit = dialog.locator('button[type="submit"]');
  await expect(submit).toBeDisabled();

  release();
  await expect(submit).toBeEnabled();
});

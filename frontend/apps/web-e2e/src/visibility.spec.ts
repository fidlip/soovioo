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
}

/** Administrace → Viditelnost údajů: sets "Seznam spolužáků" and saves. */
async function setClassmatesShared(
  page: import('@playwright/test').Page,
  shared: boolean,
) {
  await login(page, 'admin', 'admin123');
  await page.goto('/skola/admin');
  await page.getByRole('tab', { name: 'Viditelnost údajů' }).click();
  const toggle = page.locator('#share-Classmates');
  await expect(toggle).toBeAttached();
  // The switch's input is visually hidden — click its label.
  if ((await toggle.isChecked()) !== shared) {
    await page.locator('label[for="share-Classmates"]').click();
  }
  await expect(toggle).toBeChecked({ checked: shared });
  await page.getByRole('button', { name: 'Uložit' }).click();
  await expect(page.getByText('Uloženo.')).toBeVisible();
}

test('the class list reaches parents only while the school shares it', async ({
  browser,
}) => {
  for (const shared of [true, false]) {
    const admin = await browser.newPage();
    await setClassmatesShared(admin, shared);
    await admin.close();

    const parent = await browser.newPage();
    await login(parent, 'rodic', 'rodic123');
    await parent.getByRole('link', { name: 'Třída', exact: true }).click();
    await expect(parent.getByText('Učitelé třídy')).toBeVisible();
    await expect(parent.getByText(/Spolužáci \(\d+\)/)).toHaveCount(
      shared ? 1 : 0,
    );
    if (shared) {
      await expect(parent.getByText('Jan Novák')).toBeVisible();
    }
    await parent.close();
  }
});

test('staff have no Třída page in the menu', async ({ page }) => {
  await login(page, 'ucitel', 'ucitel123');
  await expect(page.getByRole('link', { name: 'Administrace' })).toBeVisible();
  await expect(
    page.getByRole('link', { name: 'Třída', exact: true }),
  ).toHaveCount(0);
});

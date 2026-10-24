import { Page } from '@playwright/test';
import { expect, test } from './fixtures';

// Both tests change the seeded school's "Viditelnost údajů".
test.describe.configure({ mode: 'serial' });

async function login(page: Page, username: string, password: string) {
  await page.goto('/skola/login');
  await page.locator('#username').fill(username);
  await page.locator('#password').fill(password);
  await page.getByRole('button', { name: 'Přihlásit se' }).click();
  await expect(page).toHaveURL(/\/skola\/home$/);
}

/** Administrace → Viditelnost údajů: the pupils' names for parents get
 * `max` as the widest scope and `def` as the default, then saved. */
async function setClassListForParents(page: Page, def: string, max: string) {
  await login(page, 'admin', 'admin123');
  await page.goto('/skola/admin');
  await page.getByRole('tab', { name: 'Viditelnost údajů' }).click();
  for (const [bound, label] of [
    ['max', max],
    ['default', def],
  ]) {
    const select = page.locator(`#vis-Student-Name-Parents-${bound}`);
    await select.click();
    await page.getByRole('option', { name: label, exact: true }).click();
    await expect(select).toHaveText(label);
    // The overlay fades out — until it's gone, the next select's options
    // would have twins.
    await expect(page.getByRole('option')).toHaveCount(0);
  }
  await page.getByRole('button', { name: 'Uložit' }).click();
  await expect(page.getByText('Uloženo.')).toBeVisible();
}

test('the class list reaches parents only while the school shares it', async ({
  browser,
}) => {
  for (const shared of [true, false]) {
    const admin = await browser.newPage();
    await setClassListForParents(
      admin,
      shared ? 'Moje třídy' : 'Nikdo',
      shared ? 'Celá škola' : 'Nikdo',
    );
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

test('a parent decides who sees their child on the class list', async ({
  browser,
}) => {
  const admin = await browser.newPage();
  await setClassListForParents(admin, 'Moje třídy', 'Celá škola');
  await admin.close();

  const page = await browser.newPage();
  await login(page, 'rodic', 'rodic123');
  await page.getByRole('link', { name: 'Mé děti' }).click();
  await expect(page.getByText('Jan Novák')).toBeVisible();

  const control = page.getByRole('button', {
    name: /^Viditelnost: Jméno/,
  });
  await expect(control).toContainText('Třídy');
  await control.click();
  const forParents = page.getByRole('group', {
    name: 'Jméno (v seznamu) — Rodiče',
  });
  await forParents.getByText('Nikdo', { exact: true }).click();
  await expect(page.getByText('Uloženo.')).toBeVisible();
  await expect(page.getByText('Vlastní volba')).toBeVisible();

  await page.getByRole('button', { name: 'Vrátit výchozí' }).click();
  await expect(page.getByText('Výchozí nastavení školy').first()).toBeVisible();
  await expect(page.getByText('Vlastní volba')).toHaveCount(0);
  await page.close();
});

test('staff have neither Třída nor Mé děti in the menu', async ({ page }) => {
  await login(page, 'ucitel', 'ucitel123');
  await expect(page.getByRole('link', { name: 'Administrace' })).toBeVisible();
  await expect(
    page.getByRole('link', { name: 'Třída', exact: true }),
  ).toHaveCount(0);
  await expect(page.getByRole('link', { name: 'Mé děti' })).toHaveCount(0);
});

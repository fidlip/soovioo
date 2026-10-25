import { Page } from '@playwright/test';
import { expect, test } from './fixtures';

/** The dev API runs 880 ports above the web dev server (see devApiPort). */
function apiUrl(page: Page): string {
  const url = new URL(page.url());
  return `http://${url.hostname}:${Number(url.port || 4200) + 880}/api`;
}

async function login(page: Page, username: string, password: string) {
  await page.goto('/skola/login');
  await page.locator('#username').fill(username);
  await page.locator('#password').fill(password);
  await page.getByRole('button', { name: 'Přihlásit se' }).click();
  await expect(page).toHaveURL(/\/skola\/home$/);
}

test('the school forces an e-mail kind: the parent sees it locked in Nastavení', async ({
  browser,
  page,
  request,
}) => {
  await login(page, 'admin', 'admin123');
  await page.goto('/skola/admin');
  await page.getByRole('tab', { name: 'Škola a matrika' }).click();
  const card = page.locator('p-card', { hasText: 'Upozornění e-mailem' });
  await card.locator('p-multiselect').click();
  const absences = page.getByRole('option', { name: 'Zapsané absence' });
  if ((await absences.getAttribute('aria-selected')) !== 'true') {
    await absences.click();
  }
  await page.keyboard.press('Escape');
  await card.getByRole('button', { name: 'Uložit' }).click();
  await expect(page.getByText('Uloženo.')).toBeVisible();

  const parent = await browser.newPage();
  try {
    await login(parent, 'rodic', 'rodic123');
    await parent.goto('/skola/settings');
    const email = parent.getByRole('switch', {
      name: 'Zapsané absence e-mailem',
    });
    await expect(email).toBeChecked();
    await expect(email).toBeDisabled();
    await expect(
      parent.getByRole('switch', { name: 'Zapsané absence v aplikaci' }),
    ).toBeEnabled();
  } finally {
    await parent.close();
    const { token } = await (
      await request.post(`${apiUrl(page)}/auth/login`, {
        data: { username: 'admin', password: 'admin123', schoolSlug: 'skola' },
      })
    ).json();
    await request.put(`${apiUrl(page)}/my-school/notifications`, {
      headers: { Authorization: `Bearer ${token}` },
      data: { digestTime: '17:00', forcedEmail: [] },
    });
  }
});

test.describe(() => {
  // Chrome logs the API's 404 as "Failed to load resource".
  test.use({ allowedConsoleMessages: [/404/] });

  test('the unsubscribe link with a wrong token says so', async ({ page }) => {
    await page.goto('/skola/unsubscribe?token=neplatny');
    await page.getByRole('button', { name: 'Odhlásit e-maily' }).click();
    await expect(page.getByText(/Odkaz je neplatný/)).toBeVisible();
  });
});

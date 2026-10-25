import { Page } from '@playwright/test';
import { expect, test } from './fixtures';

/** The dev API runs 880 ports above the web dev server (see devApiPort). */
function apiUrl(page: Page): string {
  const url = new URL(page.url());
  return `http://${url.hostname}:${Number(url.port || 4200) + 880}/api`;
}

// Both change the school's notification settings — not at the same time.
test.describe.configure({ mode: 'serial' });

const SCHOOL_DEFAULTS = {
  digestTime: '17:00',
  forcedEmail: [],
  pushFullText: false,
  pushQuietFrom: '21:00',
  pushQuietTo: '07:00',
};

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
      data: SCHOOL_DEFAULTS,
    });
  }
});

test('push: the school allows the whole text, the parent agrees and sets own night quiet', async ({
  browser,
  page,
  request,
}) => {
  await login(page, 'admin', 'admin123');
  await page.goto('/skola/admin');
  await page.getByRole('tab', { name: 'Škola a matrika' }).click();
  const card = page.locator('p-card', { hasText: 'Upozornění e-mailem' });
  await card.getByRole('switch', { name: /Povolit celý text/ }).check();
  await card.getByRole('button', { name: 'Uložit' }).click();
  await expect(page.getByText('Uloženo.')).toBeVisible();

  const parent = await browser.newPage();
  try {
    await login(parent, 'rodic', 'rodic123');
    await parent.goto('/skola/settings');
    const push = parent.getByRole('switch', {
      name: 'Zapsané absence na mobil',
    });
    await expect(push).toBeChecked();
    await push.click();
    await expect(parent.getByText('Uloženo.').first()).toBeVisible();
    await expect(push).not.toBeChecked();

    await parent.locator('#pushQuiet').click();
    await parent.getByRole('option', { name: 'Vlastní' }).click();
    await expect(parent.locator('#pushQuietFrom')).toContainText('21:00');
    await parent.locator('#pushQuietFrom').click();
    await parent.getByRole('option', { name: '22:30' }).click();
    await expect(parent.locator('#pushQuietFrom')).toContainText('22:30');

    const fullText = parent.getByRole('switch', { name: /celý text/ });
    await fullText.click();
    await expect(fullText).toBeChecked();

    await parent.reload();
    await expect(push).not.toBeChecked();
    await expect(parent.locator('#pushQuietFrom')).toContainText('22:30');
    await expect(fullText).toBeChecked();
  } finally {
    await parent.close();
    const api = apiUrl(page);
    const tokenOf = async (username: string, password: string) =>
      (
        await (
          await request.post(`${api}/auth/login`, {
            data: { username, password, schoolSlug: 'skola' },
          })
        ).json()
      ).token;
    await request.put(`${api}/my-school/notifications`, {
      headers: {
        Authorization: `Bearer ${await tokenOf('admin', 'admin123')}`,
      },
      data: SCHOOL_DEFAULTS,
    });
    await request.put(`${api}/notifications/push-settings`, {
      headers: {
        Authorization: `Bearer ${await tokenOf('rodic', 'rodic123')}`,
      },
      data: { muted: [], fullText: false, quiet: 'School' },
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

import { expect, test } from './fixtures';

async function login(page: import('@playwright/test').Page) {
  await page.goto('/skola/login');
  await page.locator('#username').fill('admin');
  await page.locator('#password').fill('admin123');
  await page.getByRole('button', { name: 'Přihlásit se' }).click();
  await expect(page).toHaveURL(/\/skola\/home$/);
}

test('collapsed sidebar keeps the logout button', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 800 });
  await login(page);
  await page.getByRole('button', { name: 'Sbalit menu' }).click();

  await page.getByRole('button', { name: 'Odhlásit se' }).click();

  await expect(page).toHaveURL(/\/skola\/login$/);
});

test('desktop sidebar collapses to icons and remembers it after reload', async ({
  page,
}) => {
  await page.setViewportSize({ width: 1280, height: 800 });
  await login(page);
  const sidebar = page.locator('#app-sidebar');
  await expect(sidebar).not.toHaveClass(/collapsed/);

  await page.getByRole('button', { name: 'Sbalit menu' }).click();
  await expect(sidebar).toHaveClass(/collapsed/);

  await page.reload();
  await expect(sidebar).toHaveClass(/collapsed/);
  // Labels are hidden, but links stay reachable by their aria-label.
  await expect(
    page.getByRole('link', { name: 'Známky', exact: true }),
  ).toBeVisible();
});

test.describe('deployed version', () => {
  test.beforeEach(async ({ page }) => {
    await page.route('**/version.json', (route) =>
      route.fulfill({
        json: {
          version: '2026.11.08-abc1234',
          commit: 'abc123',
          builtAt: '2026-11-08T20:00:00Z',
          repository: 'https://github.com/fidlip/soovioo',
          pullRequests: [
            {
              number: 244,
              title: 'Split the app shell component (#72)',
              mergedAt: '2026-11-08T19:37:30Z',
            },
          ],
        },
      }),
    );
  });

  test('an admin sees it with its pull requests, not in the collapsed sidebar', async ({
    page,
  }) => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await login(page);

    await page
      .getByRole('button', { name: /^Verze 2026\.11\.08-abc1234/ })
      .click();
    const popover = page.getByRole('region', {
      name: 'Verze 2026.11.08-abc1234',
    });
    await expect(
      popover.getByRole('link', { name: /Split the app shell component/ }),
    ).toHaveAttribute('href', 'https://github.com/fidlip/soovioo/pull/244');
    await expect(
      popover.getByRole('link', { name: 'Historie na GitHubu' }),
    ).toHaveAttribute(
      'href',
      'https://github.com/fidlip/soovioo/commits/abc123',
    );

    await page.keyboard.press('Escape');
    await page.getByRole('button', { name: 'Sbalit menu' }).click();
    await expect(page.getByRole('button', { name: /^Verze/ })).toBeHidden();
  });

  test('a teacher does not see it', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await page.goto('/skola/login');
    await page.locator('#username').fill('ucitel');
    await page.locator('#password').fill('ucitel123');
    await page.getByRole('button', { name: 'Přihlásit se' }).click();
    await expect(page).toHaveURL(/\/skola\/home$/);

    await expect(
      page.getByRole('button', { name: 'Sbalit menu' }),
    ).toBeVisible();
    await expect(page.getByRole('button', { name: /^Verze/ })).toBeHidden();
  });
});

test('mobile drawer opens from the hamburger and closes after navigating', async ({
  page,
}) => {
  await page.setViewportSize({ width: 375, height: 800 });
  await login(page);
  const gradesLink = page.getByRole('link', { name: 'Známky', exact: true });
  await expect(gradesLink).toBeHidden();

  await page.getByRole('button', { name: 'Otevřít menu' }).click();
  await gradesLink.click();

  await expect(page).toHaveURL(/\/skola\/grades$/);
  await expect(gradesLink).toBeHidden();
  // No horizontal page scroll on a phone-width screen.
  const overflow = await page.evaluate(
    () => document.documentElement.scrollWidth > window.innerWidth,
  );
  expect(overflow).toBe(false);
});

test.describe('on a touch phone', () => {
  test.use({
    viewport: { width: 375, height: 800 },
    hasTouch: true,
    isMobile: true,
  });

  test('settings and logout are in the drawer', async ({ page }) => {
    await login(page);

    await page.getByRole('button', { name: 'Otevřít menu' }).tap();
    await page.getByRole('link', { name: 'Nastavení' }).tap();
    await expect(page).toHaveURL(/\/skola\/settings$/);

    await page.getByRole('button', { name: 'Otevřít menu' }).tap();
    await page.getByRole('button', { name: 'Odhlásit se' }).tap();
    await expect(page).toHaveURL(/\/skola\/login$/);
  });
});

test('a tab opened by a link is scrolled into a phone’s tab bar', async ({
  page,
}) => {
  await page.setViewportSize({ width: 375, height: 800 });
  await login(page);
  await page.goto('/skola/admin?group=app&section=logs');
  const logs = page.getByRole('tab', { name: 'Logy' });
  await expect(logs).toHaveAttribute('aria-selected', 'true');
  // Whole, and as the last tab at the very end: no arrow over it.
  await expect(logs).toBeInViewport({ ratio: 1 });
  await expect(
    page
      .locator('p-tablist')
      .filter({ has: logs })
      .locator('.p-tablist-next-button'),
  ).toHaveCount(0);
});

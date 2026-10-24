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

import { expect, test } from './fixtures';

/**
 * Every page, in every role, must load without console errors/warnings —
 * the check itself is the auto `consoleGuard` fixture in fixtures.ts; this
 * spec just makes sure every page actually gets visited.
 */

const SCHOOL_PAGES = [
  'home',
  'grades',
  'absences',
  'timetable',
  'events',
  'messages',
  'homework',
  'behavior',
  'payments',
  'admin',
  'settings',
];

for (const username of ['admin', 'ucitel', 'rodic', 'zak']) {
  test(`${username}: every page loads with a clean console`, async ({
    page,
    waitForApiIdle,
  }) => {
    // A full page load per agenda — on a CI runner against the dev server
    // that's more than the default 30 s for the whole loop.
    test.slow();
    await page.goto('/skola/login');
    await page.locator('#username').fill(username);
    await page.locator('#password').fill(`${username}123`);
    await page.getByRole('button', { name: 'Přihlásit se' }).click();
    await expect(page).toHaveURL(/\/skola\/home$/);

    for (const path of SCHOOL_PAGES) {
      await page.goto(`/skola/${path}`);
      // The dev server's first build of a lazy page can take seconds there too.
      await expect(page.locator('main h1')).toBeVisible({ timeout: 15_000 });
      // Let the page's data requests settle before moving on.
      await waitForApiIdle();
    }
  });
}

test('superadmin: every page loads with a clean console', async ({
  page,
  waitForApiIdle,
}) => {
  test.slow();
  await page.goto('/superadmin/login');
  await page.locator('#username').fill('superadmin');
  await page.locator('#password').fill('superadmin123');
  await page.getByRole('button', { name: 'Přihlásit se' }).click();
  await expect(page).toHaveURL(/\/superadmin$/);

  for (const path of [
    'superadmin',
    'superadmin/superadmins',
    'superadmin/logs',
    'superadmin/settings',
  ]) {
    await page.goto(`/${path}`);
    await expect(page.locator('main h1')).toBeVisible();
    await waitForApiIdle();
  }
});

test('support mode: every page loads with a clean console', async ({
  page,
  request,
  waitForApiIdle,
}) => {
  test.slow();
  await page.goto('/skola/login');
  // The dev API runs 880 ports above the web dev server (see devApiPort).
  const url = new URL(page.url());
  const api = `http://${url.hostname}:${Number(url.port || 4200) + 880}/api`;
  const headersOf = async (username: string) => {
    const login = await request.post(`${api}/auth/login`, {
      data: {
        username,
        password: `${username}123`,
        schoolSlug: username === 'superadmin' ? undefined : 'skola',
      },
    });
    return { Authorization: `Bearer ${(await login.json()).token}` };
  };
  // The school asks allowing entering; the SuperAdmin takes it and enters.
  const created = await request.post(`${api}/my-school/support-tickets`, {
    headers: await headersOf('admin'),
    data: {
      title: `E2E konzole ${Date.now()}`,
      description: '',
      allowAccess: true,
    },
  });
  const ticketId = (await created.json()).ticket.id;
  const superAdmin = await headersOf('superadmin');
  await request.post(`${api}/support-tickets/${ticketId}/assign`, {
    headers: superAdmin,
  });
  // As the ticket's author (the school wants the support as a named user).
  const [author] = await (
    await request.get(`${api}/support-tickets/${ticketId}/enter-as`, {
      headers: superAdmin,
    })
  ).json();
  const entered = await request.post(
    `${api}/support-tickets/${ticketId}/enter`,
    { headers: superAdmin, data: { userId: author.id } },
  );
  expect(entered.ok()).toBe(true);
  const { code } = await entered.json();

  const [redeemed] = await Promise.all([
    page.waitForResponse(/\/api\/support-sessions\/redeem$/),
    page.goto(`/skola/support/enter#code=${code}`),
  ]);
  const { token } = await redeemed.json();
  await expect(page).toHaveURL(/\/skola\/home$/);
  await expect(page.getByText('Režim podpory', { exact: true })).toBeVisible();

  for (const path of SCHOOL_PAGES) {
    await page.goto(`/skola/${path}`);
    await expect(page.locator('main h1')).toBeVisible({ timeout: 15_000 });
    await waitForApiIdle();
  }
  // No account of its own in the school: nothing personal is offered.
  await expect(page.getByText('Vzhled')).toBeVisible();
  await expect(page.getByText('Změna hesla')).toHaveCount(0);
  await page.goto('/skola/events');
  await expect(page.locator('main h1')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Odběr' })).toHaveCount(0);

  // Ended from outside, the tab would learn it at once (a 401, see
  // support-tickets.spec) — it's closed first.
  await page.close();
  await request.post(`${api}/support-sessions/current/end`, {
    headers: { Authorization: `Bearer ${token}` },
  });
});

test('logged-out pages load with a clean console', async ({
  page,
  waitForApiIdle,
}) => {
  for (const path of [
    '/skola/login',
    '/superadmin/login',
    '/skola/accept-invite?token=x',
  ]) {
    await page.goto(path);
    await expect(page.getByRole('button').first()).toBeVisible();
    await waitForApiIdle();
  }
});

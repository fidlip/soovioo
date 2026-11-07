import { APIRequestContext, Page } from '@playwright/test';
import { expect, test } from './fixtures';

// A wrong password (401 at login) and a code that means nothing (400).
test.use({ allowedConsoleMessages: [/status of 40[01]/] });

/** The dev API runs 880 ports above the web dev server (see devApiPort). */
function apiUrl(page: Page): string {
  const url = new URL(page.url());
  return `http://${url.hostname}:${Number(url.port || 4200) + 880}/api`;
}

async function login(page: Page, slug: string, password: string) {
  await page.goto(`/${slug}/login`);
  await page.locator('#username').fill('spravce');
  await page.locator('#password').fill(password);
  await page.getByRole('button', { name: 'Přihlásit se' }).click();
  await expect(page).toHaveURL(new RegExp(`/${slug}/home$`));
}

/** Two new schools, each with its admin `spravce` — the links stay away
 * from the seeded accounts other tests use. */
async function twoSchools(request: APIRequestContext, page: Page, tag: string) {
  await page.goto('/skola/login');
  const api = apiUrl(page);
  const superAdmin = await (
    await request.post(`${api}/auth/login`, {
      data: { username: 'superadmin', password: 'superadmin123' },
    })
  ).json();
  const stamp = Date.now();
  const schools = [`${tag}-a-${stamp}`, `${tag}-b-${stamp}`].map((slug) => ({
    slug,
    name: `Škola ${slug}`,
    password: `${slug}-heslo`,
  }));
  for (const school of schools) {
    const created = await request.post(`${api}/schools`, {
      headers: { Authorization: `Bearer ${superAdmin.token}` },
      data: {
        name: school.name,
        slug: school.slug,
        adminUsername: 'spravce',
        adminPassword: school.password,
      },
    });
    expect(created.ok()).toBe(true);
  }
  return schools;
}

/** Picks the school in the dialog and goes to log in there. */
async function chooseSchool(
  page: Page,
  school: { slug: string; name: string },
) {
  const dialog = page.getByRole('dialog', { name: 'Propojit další účet' });
  await dialog.locator('#linkSchool').pressSequentially(school.slug);
  await page.getByRole('option', { name: school.name }).click();
  await dialog
    .getByRole('button', { name: 'Pokračovat na přihlášení' })
    .click();
}

test("linked through the other school's login, switching there and back without a password, then unlinking", async ({
  page,
  request,
}) => {
  const [first, second] = await twoSchools(request, page, 'odkaz');
  await login(page, first.slug, first.password);
  const switcher = page.getByRole('button', { name: /Přepnout účet/ });

  // The school's name is there without a link too, its menu offers linking.
  await expect(switcher).toHaveAccessibleName(
    `Přepnout účet, teď: ${first.name}`,
  );
  await switcher.click();
  await page.getByRole('menuitem', { name: 'Propojit další účet' }).click();
  await chooseSchool(page, second);

  // The other school's own login says what it's for.
  await expect(page).toHaveURL(new RegExp(`/${second.slug}/login`));
  await expect(
    page.getByText(
      `Přihlášení kvůli propojení s účtem spravce (${first.name})`,
    ),
  ).toBeVisible();
  await page.locator('#username').fill('spravce');
  await page.locator('#password').fill('spatne');
  await page.getByRole('button', { name: 'Přihlásit se' }).click();
  await expect(page.getByText('Neplatné přihlašovací údaje.')).toBeVisible();
  await page.locator('#password').fill(second.password);
  await page.getByRole('button', { name: 'Přihlásit se' }).click();

  // Nothing is linked until it's confirmed, and back at the first school.
  await expect(
    page.getByRole('heading', { name: 'Propojit účty?' }),
  ).toBeVisible();
  await page.getByRole('button', { name: 'Propojit', exact: true }).click();
  await expect(page).toHaveURL(new RegExp(`/${first.slug}/settings$`));
  await expect(page.getByText(`${second.name} – spravce`)).toBeVisible();

  // There, on that school's own session: no password asked.
  await switcher.click();
  await page.getByRole('menuitem', { name: second.name }).click();
  await expect(page).toHaveURL(new RegExp(`/${second.slug}/home$`));
  expect(page.url()).not.toContain('code');
  await expect(switcher).toHaveAccessibleName(
    `Přepnout účet, teď: ${second.name}`,
  );

  // A reload stays there, on the cookie of this school.
  await page.reload();
  await expect(switcher).toHaveAccessibleName(
    `Přepnout účet, teď: ${second.name}`,
  );

  // And back: the first school's session never ended.
  await switcher.click();
  await page.getByRole('menuitem', { name: first.name }).click();
  await expect(page).toHaveURL(new RegExp(`/${first.slug}/home$`));
  await expect(switcher).toHaveAccessibleName(
    `Přepnout účet, teď: ${first.name}`,
  );

  await page.getByRole('link', { name: 'Nastavení' }).click();
  await page.getByRole('button', { name: 'Zrušit propojení' }).click();
  await page
    .getByRole('alertdialog')
    .getByRole('button', { name: 'Zrušit propojení' })
    .click();
  await expect(page.getByText(`${second.name} – spravce`)).toHaveCount(0);
  await switcher.click();
  await expect(page.getByRole('menuitem', { name: second.name })).toHaveCount(
    0,
  );
});

test('someone already logged in at the other school only confirms, from Nastavení', async ({
  page,
  request,
}) => {
  const [first, second] = await twoSchools(request, page, 'potvrzeni');
  await login(page, second.slug, second.password);
  await login(page, first.slug, first.password);

  await page.getByRole('link', { name: 'Nastavení' }).click();
  await page
    .locator('app-linked-accounts-card')
    .getByRole('button', { name: 'Propojit další účet' })
    .click();
  await chooseSchool(page, second);

  await expect(
    page.getByRole('heading', { name: 'Propojit účty?' }),
  ).toBeVisible();
  await page.getByRole('button', { name: 'Zrušit' }).click();
  await expect(page).toHaveURL(new RegExp(`/${first.slug}/settings$`));
  await expect(page.getByText(`${second.name} – spravce`)).toHaveCount(0);
});

test('a code that means nothing offers logging in', async ({ page }) => {
  await page.goto('/skola/account/switch#code=neplatny');

  await expect(
    page.getByText('Přepnutí na druhý účet se nepovedlo'),
  ).toBeVisible();
  await expect(
    page.getByRole('button', { name: 'Přihlásit se' }),
  ).toBeVisible();
  expect(page.url()).not.toContain('neplatny');
});

test("the school's log shows neither the link nor the switch", async ({
  page,
  request,
}) => {
  const [first, second] = await twoSchools(request, page, 'log');
  const api = apiUrl(page);
  const auth = (token: string) => ({
    headers: { Authorization: `Bearer ${token}` },
  });
  const admin = (
    await (
      await request.post(`${api}/auth/login`, {
        data: {
          username: 'spravce',
          password: first.password,
          schoolSlug: first.slug,
        },
      })
    ).json()
  ).token as string;

  // The whole way, as the two schools' pages go it.
  const other = (
    await (
      await request.post(`${api}/auth/login`, {
        data: {
          username: 'spravce',
          password: second.password,
          schoolSlug: second.slug,
        },
      })
    ).json()
  ).token as string;
  const started = await (
    await request.post(`${api}/linked-accounts/requests`, {
      ...auth(admin),
      data: { schoolSlug: second.slug },
    })
  ).json();
  const approved = await (
    await request.post(`${api}/linked-accounts/requests/approve`, {
      ...auth(other),
      data: { code: started.code },
    })
  ).json();
  const completed = await request.post(
    `${api}/linked-accounts/requests/complete`,
    { ...auth(admin), data: { code: approved.code } },
  );
  expect(completed.status()).toBe(204);
  const counts = await (
    await request.get(`${api}/notifications/unread-counts`, auth(admin))
  ).json();
  const otherId = counts.linkedAccounts[0].userId;
  const switched = await request.post(
    `${api}/linked-accounts/${otherId}/switch`,
    auth(admin),
  );
  expect(switched.ok()).toBe(true);
  const unlinked = await request.delete(
    `${api}/linked-accounts/${otherId}`,
    auth(admin),
  );
  expect(unlinked.status()).toBe(204);

  const log = await (
    await request.get(`${api}/audit-logs?pageSize=100`, auth(admin))
  ).json();
  expect(log.items.length).toBeGreaterThan(0);
  expect(JSON.stringify(log.items)).not.toContain('linked-accounts');
  expect(JSON.stringify(log.items)).not.toContain(second.slug);
  // The other school's log has its own login and nothing more.
  const otherAdminLog = await (
    await request.get(`${api}/audit-logs?pageSize=100`, auth(other))
  ).json();
  expect(JSON.stringify(otherAdminLog.items)).not.toContain('linked-accounts');
  expect(JSON.stringify(otherAdminLog.items)).not.toContain(first.slug);
});

import { APIRequestContext, Page } from '@playwright/test';
import { expect, test } from './fixtures';

// What every page offers follows the permissions of the active role: a
// staff role of the school's own without any shows no tools, the same role
// given all of them (whole school) shows every one — after a reload, without
// logging in again. And a permission taken away is refused right away.

async function login(page: Page, username: string, password: string) {
  await page.goto('/skola/login');
  await page.locator('#username').fill(username);
  await page.locator('#password').fill(password);
  await page.getByRole('button', { name: 'Přihlásit se' }).click();
  await expect(page).toHaveURL(/\/skola\/home$/);
}

/** The dev API runs 880 ports above the web dev server (see devApiPort). */
function apiUrl(page: Page): string {
  const url = new URL(page.url());
  return `http://${url.hostname}:${Number(url.port || 4200) + 880}/api`;
}

interface Admin {
  api: string;
  headers: Record<string, string>;
  request: APIRequestContext;
}

async function adminApi(
  page: Page,
  request: APIRequestContext,
): Promise<Admin> {
  await page.goto('/skola/login');
  const api = apiUrl(page);
  const { token } = await (
    await request.post(`${api}/auth/login`, {
      data: { username: 'admin', password: 'admin123', schoolSlug: 'skola' },
    })
  ).json();
  return { api, headers: { Authorization: `Bearer ${token}` }, request };
}

type Scopes = Record<string, string>;

/** Every permission a staff role can have, at its widest (or none). */
async function staffScopes(admin: Admin, widest: boolean): Promise<Scopes> {
  const catalog: { permission: string; staffScopes: string[] }[] = await (
    await admin.request.get(`${admin.api}/roles/catalog`, {
      headers: admin.headers,
    })
  ).json();
  return Object.fromEntries(
    catalog.map((i) => [
      i.permission,
      widest ? i.staffScopes[i.staffScopes.length - 1] : 'None',
    ]),
  );
}

async function setRole(
  admin: Admin,
  id: number,
  name: string,
  permissions: Scopes,
) {
  const response = await admin.request.put(`${admin.api}/roles/${id}`, {
    headers: admin.headers,
    data: { name, permissions },
  });
  expect(response.ok()).toBe(true);
}

/** A staff role of the school's own and an account in it; returns the role's id. */
async function staffAccount(
  admin: Admin,
  roleName: string,
  username: string,
): Promise<number> {
  const role = await (
    await admin.request.post(`${admin.api}/roles`, {
      headers: admin.headers,
      data: { name: roleName, kind: 'Staff' },
    })
  ).json();
  const person = await (
    await admin.request.post(`${admin.api}/people`, {
      headers: admin.headers,
      data: { firstName: 'Petra', lastName: roleName },
    })
  ).json();
  expect(
    (
      await admin.request.post(`${admin.api}/users`, {
        headers: admin.headers,
        data: {
          username,
          password: 'docasne123',
          roleIds: [role.id],
          personId: person.id,
        },
      })
    ).ok(),
  ).toBe(true);
  const login = await (
    await admin.request.post(`${admin.api}/auth/login`, {
      data: { username, password: 'docasne123', schoolSlug: 'skola' },
    })
  ).json();
  await admin.request.post(`${admin.api}/users/me/change-password`, {
    headers: { Authorization: `Bearer ${login.token}` },
    data: { currentPassword: 'docasne123', newPassword: 'nove-heslo1' },
  });
  return role.id;
}

const nav = (page: Page) =>
  page.getByRole('navigation', { name: 'Hlavní navigace' });

async function open(
  page: Page,
  link: string,
  waitForApiIdle: () => Promise<void>,
) {
  await nav(page).getByRole('link', { name: link, exact: true }).click();
  await waitForApiIdle();
}

/** Each page's tool, by the page's menu link. */
const TOOLS: [string, string][] = [
  ['Známky', 'Zapsat známku'],
  ['Docházka', 'Zapsat absenci'],
  ['Chování', 'Nový záznam'],
  ['Chování', 'Nastavit klasifikaci'],
  ['Domácí úkoly', 'Nový úkol'],
  ['Kalendář', 'Nová akce'],
  ['Platby', 'Nová platba'],
  ['Rozvrh', 'Upravit rozvrh'],
  ['Rozvrh', 'Verze rozvrhu'],
];

/** The "Komu" choices of a new message. */
async function sendModes(page: Page): Promise<string[]> {
  await page.getByRole('button', { name: 'Nová zpráva' }).click();
  const dialog = page.getByRole('dialog', { name: 'Nová zpráva' });
  await dialog.locator('#sendMode').click();
  await expect(page.getByRole('option').first()).toBeVisible();
  const modes = await page.getByRole('option').allInnerTexts();
  await page.keyboard.press('Escape');
  await expect(page.getByRole('listbox')).toBeHidden();
  await page.keyboard.press('Escape');
  await expect(dialog).toBeHidden();
  return modes;
}

/** Opens the "Příjemci" list of a just opened dialog, its name filled first.
 * The dialog focuses the name once its open animation ends — on a slow runner
 * after the click, which closed the list again. Left empty, the name's
 * "Povinné pole" shows up on leaving it and moves the list away mid-click. */
async function openAudience(page: Page, dialogName: string) {
  const dialog = page.getByRole('dialog', { name: dialogName });
  const name = dialog.getByRole('textbox', { name: /^Název/ });
  await expect(name).toBeFocused();
  await name.fill('Test');
  await dialog.locator('p-select[name="audience"]').click();
}

test('the pages offer exactly what the role may do', async ({
  page,
  request,
  waitForApiIdle,
}) => {
  // Every page twice, without and with the permissions — over 30 s on a busy
  // CI runner.
  test.slow();
  const admin = await adminApi(page, request);
  const roleName = `Bez práv ${Date.now()}`;
  const username = `prava${Date.now()}`;
  const roleId = await staffAccount(admin, roleName, username);
  await setRole(admin, roleId, roleName, await staffScopes(admin, false));

  await login(page, username, 'nove-heslo1');
  await expect(
    nav(page).getByRole('link', { name: 'Logy', exact: true }),
  ).toHaveCount(0);
  await expect(
    nav(page).getByRole('link', { name: 'Přidělení výuky', exact: true }),
  ).toHaveCount(0);
  await expect(
    nav(page).getByRole('link', { name: 'Administrace', exact: true }),
  ).toHaveCount(0);
  for (const [link, tool] of TOOLS) {
    await open(page, link, waitForApiIdle);
    await expect(
      page.getByRole('button', { name: tool }),
      `${link}: ${tool}`,
    ).toHaveCount(0);
  }
  await open(page, 'Zprávy', waitForApiIdle);
  expect(await sendModes(page)).not.toContain(
    'Celé třídě (každé rodině zvlášť)',
  );

  // Everything, for the whole school — a reload is enough.
  await setRole(admin, roleId, roleName, await staffScopes(admin, true));
  await page.reload();
  await expect(
    nav(page).getByRole('link', { name: 'Logy', exact: true }),
  ).toBeVisible();
  await expect(
    nav(page).getByRole('link', { name: 'Přidělení výuky', exact: true }),
  ).toBeVisible();
  await expect(
    nav(page).getByRole('link', { name: 'Administrace', exact: true }),
  ).toBeVisible();
  for (const [link, tool] of TOOLS) {
    await open(page, link, waitForApiIdle);
    await expect(
      page.getByRole('button', { name: tool }),
      `${link}: ${tool}`,
    ).toBeVisible();
  }

  await open(page, 'Kalendář', waitForApiIdle);
  await page.getByRole('button', { name: 'Nová akce' }).click();
  await openAudience(page, 'Nová akce');
  await expect(page.getByRole('option', { name: 'Všichni' })).toBeVisible();
  await page.keyboard.press('Escape');
  await page.reload();

  await open(page, 'Platby', waitForApiIdle);
  await page.getByRole('button', { name: 'Nová platba' }).click();
  await openAudience(page, 'Nová platba');
  await expect(
    page.getByRole('option', { name: 'Všichni žáci' }),
  ).toBeVisible();
  await page.keyboard.press('Escape');
  await page.reload();

  await open(page, 'Domácí úkoly', waitForApiIdle);
  await page.getByRole('button', { name: 'Nový úkol' }).click();
  // Any class and subject, not only what the timetable says they teach.
  await expect(page.locator('#newClass')).toBeVisible();
  await page.reload();

  await open(page, 'Zprávy', waitForApiIdle);
  const modes = await sendModes(page);
  expect(modes).toContain('Celé třídě (každé rodině zvlášť)');
  expect(modes).toContain('Celé škole (každé rodině zvlášť)');

  await open(page, 'Administrace', waitForApiIdle);
  await expect(page.getByRole('tab', { name: 'Role' })).toHaveCount(0);
  await expect(
    page.getByText('Jste třídní učitel').or(page.getByText(/dostupná/)),
  ).toBeVisible();
});

test.describe('a permission taken away', () => {
  // The refused save is a 403, which the browser logs.
  test.use({ allowedConsoleMessages: [/403|failed/] });

  test('is refused right away and the page stops offering it', async ({
    page,
    request,
    waitForApiIdle,
  }) => {
    const admin = await adminApi(page, request);
    const roleName = `Rozvrhář ${Date.now()}`;
    const username = `rozvrh${Date.now()}`;
    const roleId = await staffAccount(admin, roleName, username);
    const scopes = await staffScopes(admin, false);
    await setRole(admin, roleId, roleName, { ...scopes, Timetable: 'School' });

    await login(page, username, 'nove-heslo1');
    await open(page, 'Rozvrh', waitForApiIdle);
    await page.getByRole('button', { name: 'Upravit rozvrh' }).click();

    // Taken away while the page is open, before the token would expire.
    await setRole(admin, roleId, roleName, scopes);
    const addButton = page
      .getByRole('button', { name: 'Přidat hodinu' })
      .first();
    await addButton.locator('xpath=ancestor::td').hover();
    await addButton.click();
    const dialog = page.getByRole('dialog', { name: 'Nová hodina' });
    await dialog.locator('p-select[name="formSubjectId"]').click();
    await page.getByRole('option').first().click();
    const saved = page.waitForResponse(
      (r) => r.request().method() === 'POST' && r.url().includes('/timetable'),
    );
    await dialog.locator('button[type="submit"]').click();
    expect((await saved).status()).toBe(403);
    await expect(
      page.getByText('Na tuto akci nemáte oprávnění.', { exact: false }),
    ).toBeVisible();

    // The 403 refreshed the session — the next page shows no editing.
    await waitForApiIdle();
    await dialog.getByRole('button', { name: 'Zrušit' }).click();
    await expect(dialog).toBeHidden();
    await open(page, 'Domů', waitForApiIdle);
    await open(page, 'Rozvrh', waitForApiIdle);
    await expect(
      page.getByRole('button', { name: 'Upravit rozvrh' }),
    ).toHaveCount(0);
  });
});

test('an account with two roles switches between them, and the pages follow', async ({
  page,
  request,
  waitForApiIdle,
}) => {
  const admin = await adminApi(page, request);
  const roles: { id: number; builtIn: string | null }[] = await (
    await admin.request.get(`${admin.api}/roles`, { headers: admin.headers })
  ).json();
  const roleOf = (builtIn: string) =>
    roles.find((r) => r.builtIn === builtIn)!.id;
  const students: { id: number; firstName: string; lastName: string }[] =
    await (
      await admin.request.get(`${admin.api}/students`, {
        headers: admin.headers,
      })
    ).json();
  const jan = students.find(
    (s) => s.firstName === 'Jan' && s.lastName === 'Novák',
  )!;
  const person = await (
    await admin.request.post(`${admin.api}/people`, {
      headers: admin.headers,
      data: {
        firstName: 'Dana',
        lastName: `Dvojrole ${Date.now()}`,
        teacher: {},
        parent: { childIds: [jan.id] },
      },
    })
  ).json();
  const username = `dvojrole${Date.now()}`;
  expect(
    (
      await admin.request.post(`${admin.api}/users`, {
        headers: admin.headers,
        data: {
          username,
          password: 'docasne123',
          roleIds: [roleOf('Teacher'), roleOf('Parent')],
          personId: person.id,
        },
      })
    ).ok(),
  ).toBe(true);
  const login1 = await (
    await admin.request.post(`${admin.api}/auth/login`, {
      data: { username, password: 'docasne123', schoolSlug: 'skola' },
    })
  ).json();
  await admin.request.post(`${admin.api}/users/me/change-password`, {
    headers: { Authorization: `Bearer ${login1.token}` },
    data: { currentPassword: 'docasne123', newPassword: 'nove-heslo1' },
  });

  await login(page, username, 'nove-heslo1');
  const switcher = page.getByRole('button', { name: /^Přepnout roli/ });
  await expect(switcher).toHaveAccessibleName('Přepnout roli, teď: Učitel');
  await expect(
    nav(page).getByRole('link', { name: 'Mé děti', exact: true }),
  ).toHaveCount(0);

  await switcher.click();
  await page.getByRole('menuitem', { name: 'Rodič' }).click();
  await expect(switcher).toHaveAccessibleName('Přepnout roli, teď: Rodič');
  await expect(
    nav(page).getByRole('link', { name: 'Mé děti', exact: true }),
  ).toBeVisible();
  await open(page, 'Známky', waitForApiIdle);
  await expect(page.getByRole('button', { name: 'Zapsat známku' })).toHaveCount(
    0,
  );

  await switcher.click();
  await page.getByRole('menuitem', { name: 'Učitel' }).click();
  await expect(switcher).toHaveAccessibleName('Přepnout roli, teď: Učitel');
  await open(page, 'Známky', waitForApiIdle);
  await expect(
    page.getByRole('button', { name: 'Zapsat známku' }),
  ).toBeVisible();
});

test('the school overview is the admin’s, the day’s lessons everyone else’s', async ({
  page,
}) => {
  await login(page, 'admin', 'admin123');
  await expect(page.getByText('Přehled školy', { exact: true })).toBeVisible();
  await page.context().clearCookies();
  await login(page, 'ucitel', 'ucitel123');
  await expect(page.getByText('Dnešní rozvrh', { exact: true })).toBeVisible();
  await expect(page.getByText('Přehled školy', { exact: true })).toHaveCount(0);
});

test('a parents’ representative may address their children’s classes, not the whole school', async ({
  page,
  request,
  waitForApiIdle,
}) => {
  const admin = await adminApi(page, request);
  const students: { id: number; firstName: string; lastName: string }[] =
    await (
      await admin.request.get(`${admin.api}/students`, {
        headers: admin.headers,
      })
    ).json();
  const jan = students.find(
    (s) => s.firstName === 'Jan' && s.lastName === 'Novák',
  )!;
  const roleName = `Důvěrník ${Date.now()}`;
  const role = await (
    await admin.request.post(`${admin.api}/roles`, {
      headers: admin.headers,
      data: { name: roleName, kind: 'Parent' },
    })
  ).json();
  await setRole(admin, role.id, roleName, {
    EventsWrite: 'Own',
    PaymentsWrite: 'Own',
  });
  const person = await (
    await admin.request.post(`${admin.api}/people`, {
      headers: admin.headers,
      data: {
        firstName: 'Olga',
        lastName: roleName,
        parent: { childIds: [jan.id] },
      },
    })
  ).json();
  const username = `duvernik${Date.now()}`;
  await admin.request.post(`${admin.api}/users`, {
    headers: admin.headers,
    data: {
      username,
      password: 'docasne123',
      roleIds: [role.id],
      personId: person.id,
    },
  });
  const first = await (
    await admin.request.post(`${admin.api}/auth/login`, {
      data: { username, password: 'docasne123', schoolSlug: 'skola' },
    })
  ).json();
  await admin.request.post(`${admin.api}/users/me/change-password`, {
    headers: { Authorization: `Bearer ${first.token}` },
    data: { currentPassword: 'docasne123', newPassword: 'nove-heslo1' },
  });

  await login(page, username, 'nove-heslo1');
  await open(page, 'Kalendář', waitForApiIdle);
  await page.getByRole('button', { name: 'Nová akce' }).click();
  await openAudience(page, 'Nová akce');
  await expect(page.getByRole('option', { name: 'Výběr' })).toBeVisible();
  await expect(page.getByRole('option', { name: 'Všichni' })).toHaveCount(0);
  await page.reload();

  await open(page, 'Platby', waitForApiIdle);
  await page.getByRole('button', { name: 'Nová platba' }).click();
  await openAudience(page, 'Nová platba');
  await expect(page.getByRole('option', { name: 'Všichni žáci' })).toHaveCount(
    0,
  );
  await expect(page.getByRole('option').first()).toBeVisible();
});

import { APIRequestContext, Page } from '@playwright/test';
import { expect, test } from './fixtures';

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

async function tokenOf(
  request: APIRequestContext,
  api: string,
  username: string,
  password: string,
): Promise<string> {
  const response = await request.post(`${api}/auth/login`, {
    data: { username, password, schoolSlug: 'skola' },
  });
  return (await response.json()).token;
}

/** Administrace → Role as the admin: a new role as a copy of `copyOf`,
 * with one permission set to `scope` in its matrix. */
async function createRole(
  page: Page,
  name: string,
  kind: string,
  copyOf: string,
  permission: string,
  scope: string,
) {
  await login(page, 'admin', 'admin123');
  await page.getByRole('link', { name: 'Administrace', exact: true }).click();
  await page.getByRole('tab', { name: 'Role' }).click();
  const panel = page.getByRole('tabpanel', { name: 'Role' });
  await expect(panel.locator('tr', { hasText: copyOf }).first()).toBeVisible();

  await panel.getByRole('button', { name: 'Nová role' }).click();
  const create = page.getByRole('dialog', { name: 'Nová role' });
  await create.getByRole('textbox', { name: /^Název/ }).fill(name);
  await create.locator('p-select[name="kind"]').click();
  await page.getByRole('option', { name: kind, exact: true }).click();
  await create.locator('p-select[name="copyFrom"]').click();
  await page.getByRole('option', { name: copyOf, exact: true }).click();
  await create.getByRole('button', { name: 'Vytvořit' }).click();

  // The matrix opens right after creating.
  const edit = page.getByRole('dialog', { name: `Role ${name}` });
  await edit.getByText(permission).locator('..').getByRole('combobox').click();
  await page.getByRole('option', { name: scope, exact: true }).click();
  await edit.getByRole('button', { name: 'Uložit' }).click();
  await expect(edit).toBeHidden();
  await expect(panel.locator('tr', { hasText: name })).toBeVisible();
}

/** An account in the role, made straight through the API, logged in. */
async function loginInNewAccount(
  page: Page,
  request: APIRequestContext,
  roleName: string,
  person: object,
) {
  const api = apiUrl(page);
  const admin = {
    Authorization: `Bearer ${await tokenOf(request, api, 'admin', 'admin123')}`,
  };
  const roles = await (
    await request.get(`${api}/roles`, { headers: admin })
  ).json();
  const roleId = roles.find((r: { name: string }) => r.name === roleName).id;
  const created = await (
    await request.post(`${api}/people`, { headers: admin, data: person })
  ).json();
  const username = `ucet${Date.now()}`;
  expect(
    (
      await request.post(`${api}/users`, {
        headers: admin,
        data: {
          username,
          password: 'docasne123',
          roleIds: [roleId],
          personId: created.id,
        },
      })
    ).ok(),
  ).toBe(true);
  const own = {
    Authorization: `Bearer ${await tokenOf(request, api, username, 'docasne123')}`,
  };
  await request.post(`${api}/users/me/change-password`, {
    headers: own,
    data: { currentPassword: 'docasne123', newPassword: 'nove-heslo1' },
  });

  await page.context().clearCookies();
  await login(page, username, 'nove-heslo1');
}

test('a role of the school’s own: created in Administrace, its account sees only what it was given', async ({
  page,
  request,
  waitForApiIdle,
}) => {
  const roleName = `Asistent ${Date.now()}`;
  await createRole(
    page,
    roleName,
    'Zaměstnanec',
    'Učitel',
    'Zapsat, upravit a smazat známku',
    '—',
  );
  await loginInNewAccount(page, request, roleName, {
    firstName: 'Iva',
    lastName: roleName,
  });

  await page
    .getByRole('navigation', { name: 'Hlavní navigace' })
    .getByRole('link', { name: 'Známky', exact: true })
    .click();
  await expect(page).toHaveURL(/\/skola\/grades$/);
  await waitForApiIdle();
  await expect(page.getByRole('button', { name: 'Zapsat známku' })).toHaveCount(
    0,
  );
});

test('a parents’ representative writes to their child’s class', async ({
  page,
  request,
}) => {
  const roleName = `Důvěrník ${Date.now()}`;
  await createRole(
    page,
    roleName,
    'Rodič',
    'Rodič',
    'Hromadná zpráva třídě',
    'Tříd svých dětí',
  );
  const api = apiUrl(page);
  const admin = {
    Authorization: `Bearer ${await tokenOf(request, api, 'admin', 'admin123')}`,
  };
  const students = await (
    await request.get(`${api}/students`, { headers: admin })
  ).json();
  const jan = students.find(
    (s: { firstName: string; lastName: string }) =>
      s.firstName === 'Jan' && s.lastName === 'Novák',
  );
  await loginInNewAccount(page, request, roleName, {
    firstName: 'Věra',
    lastName: roleName,
    parent: { childIds: [jan.id] },
  });

  await page
    .getByRole('navigation', { name: 'Hlavní navigace' })
    .getByRole('link', { name: 'Zprávy', exact: true })
    .click();
  await page.getByRole('button', { name: 'Nová zpráva' }).click();
  const dialog = page.getByRole('dialog', { name: 'Nová zpráva' });
  await dialog.locator('#sendMode').click();
  await page
    .getByRole('option', { name: 'Celé třídě (každé rodině zvlášť)' })
    .click();
  await dialog.locator('#broadcastClass').click();
  await page.getByRole('option').first().click();
  await dialog.locator('#subject').fill(`Výlet ${roleName}`);
  await dialog.locator('#body').fill('Sraz v 8 před školou.');
  await dialog.getByRole('button', { name: 'Odeslat' }).click();
  await expect(dialog).toBeHidden();
  await expect(page.getByText(/Odesláno \d+ rodinám/)).toBeVisible();
});

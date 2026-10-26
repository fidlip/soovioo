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

test('a role of the school’s own: created in Administrace, its account sees only what it was given', async ({
  page,
  request,
  waitForApiIdle,
}) => {
  const roleName = `Asistent ${Date.now()}`;
  await login(page, 'admin', 'admin123');
  await page.getByRole('link', { name: 'Administrace', exact: true }).click();
  await page.getByRole('tab', { name: 'Role' }).click();
  const panel = page.getByRole('tabpanel', { name: 'Role' });
  await expect(panel.locator('tr', { hasText: 'Učitel' })).toBeVisible();

  // A copy of Učitel…
  await panel.getByRole('button', { name: 'Nová role' }).click();
  const create = page.getByRole('dialog', { name: 'Nová role' });
  await create.getByRole('textbox', { name: /^Název/ }).fill(roleName);
  await create.locator('p-select[name="copyFrom"]').click();
  await page.getByRole('option', { name: 'Učitel', exact: true }).click();
  await create.getByRole('button', { name: 'Vytvořit' }).click();

  // …that doesn't write grades (the matrix opens right after creating).
  const edit = page.getByRole('dialog', { name: `Role ${roleName}` });
  await edit
    .getByText('Zapsat, upravit a smazat známku')
    .locator('..')
    .getByRole('combobox')
    .click();
  await page.getByRole('option', { name: '—', exact: true }).click();
  await edit.getByRole('button', { name: 'Uložit' }).click();
  await expect(edit).toBeHidden();
  await expect(panel.locator('tr', { hasText: roleName })).toBeVisible();

  // An account in it, straight through the API.
  const api = apiUrl(page);
  const admin = {
    Authorization: `Bearer ${await tokenOf(request, api, 'admin', 'admin123')}`,
  };
  const roles = await (
    await request.get(`${api}/roles`, { headers: admin })
  ).json();
  const roleId = roles.find((r: { name: string }) => r.name === roleName).id;
  const person = await (
    await request.post(`${api}/people`, {
      headers: admin,
      data: { firstName: 'Iva', lastName: roleName },
    })
  ).json();
  const username = `asistent${Date.now()}`;
  expect(
    (
      await request.post(`${api}/users`, {
        headers: admin,
        data: {
          username,
          password: 'docasne123',
          roleIds: [roleId],
          personId: person.id,
        },
      })
    ).ok(),
  ).toBe(true);
  const own = {
    Authorization: `Bearer ${await tokenOf(request, api, username, 'docasne123')}`,
  };
  await request.post(`${api}/users/me/change-password`, {
    headers: own,
    data: { currentPassword: 'docasne123', newPassword: 'asistent-heslo1' },
  });

  await page.context().clearCookies();
  await login(page, username, 'asistent-heslo1');
  await page.getByRole('link', { name: 'Známky', exact: true }).click();
  await expect(page).toHaveURL(/\/skola\/grades$/);
  await waitForApiIdle();
  await expect(page.getByRole('button', { name: 'Zapsat známku' })).toHaveCount(
    0,
  );
});

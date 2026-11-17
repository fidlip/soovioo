import { Page } from '@playwright/test';
import { expect, test } from './fixtures';

/** The dev API runs 880 ports above the web dev server (see devApiPort). */
function apiUrl(page: Page): string {
  const url = new URL(page.url());
  return `http://${url.hostname}:${Number(url.port || 4200) + 880}/api`;
}

test("a new school's admin goes through Nastavení školy, then founds the school year", async ({
  page,
  request,
}) => {
  await page.goto('/skola/login');
  const api = apiUrl(page);
  const superAdmin = await (
    await request.post(`${api}/auth/login`, {
      data: { username: 'superadmin', password: 'superadmin123' },
    })
  ).json();
  const slug = `nastaveni-${Date.now()}`;
  const created = await request.post(`${api}/schools`, {
    headers: { Authorization: `Bearer ${superAdmin.token}` },
    data: {
      name: `Škola ${slug}`,
      slug,
      adminUsername: 'spravce',
      adminPassword: `${slug}-heslo`,
    },
  });
  expect(created.ok()).toBe(true);

  await page.goto(`/${slug}/login`);
  await page.locator('#username').fill('spravce');
  await page.locator('#password').fill(`${slug}-heslo`);
  await page.getByRole('button', { name: 'Přihlásit se' }).click();

  const wizard = page.getByRole('dialog', { name: 'Nastavení školy' });
  await expect(wizard).toBeVisible();
  await wizard.getByRole('radio', { name: /Střední škola/ }).click();
  await expect(wizard.getByLabel('Název školy')).toHaveValue(`Škola ${slug}`);
  await wizard.getByLabel('Název školy').fill(`Gymnázium ${slug}`);
  await wizard.getByLabel('Obec').fill('Blansko');
  await wizard.getByRole('button', { name: 'Další' }).click();
  await wizard.getByLabel('E-mail').fill('neni-email');
  await expect(wizard.getByRole('button', { name: 'Další' })).toBeDisabled();
  await wizard.getByLabel('E-mail').fill('skola@example.cz');
  await wizard.getByRole('button', { name: 'Další' }).click();
  await wizard.getByRole('radio', { name: /Číslem/ }).click();
  await wizard.getByLabel('Číslo bankovního účtu').fill('19-2000145399/0800');
  await wizard.getByRole('button', { name: 'Dokončit' }).click();

  await expect(wizard).toBeHidden();
  await expect(page.getByText(`Gymnázium ${slug}`).first()).toBeVisible();
  await expect(
    page.getByRole('dialog', { name: /Založení školního roku/ }),
  ).toBeVisible();

  const { token } = await (
    await request.post(`${api}/auth/login`, {
      data: {
        username: 'spravce',
        password: `${slug}-heslo`,
        schoolSlug: slug,
      },
    })
  ).json();
  const school = await (
    await request.get(`${api}/my-school`, {
      headers: { Authorization: `Bearer ${token}` },
    })
  ).json();
  expect(school).toMatchObject({
    name: `Gymnázium ${slug}`,
    type: 'Secondary',
    city: 'Blansko',
    email: 'skola@example.cz',
    bankAccountNumber: 'CZ6508000000192000145399',
  });
  expect(school.setupCompletedAt).not.toBeNull();
});

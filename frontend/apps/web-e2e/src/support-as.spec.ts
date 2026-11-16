import { Page } from '@playwright/test';
import { expect, test } from './fixtures';

async function login(page: Page, area: 'skola' | 'superadmin', user: string) {
  await page.goto(`/${area}/login`);
  await page.locator('#username').fill(user);
  await page.locator('#password').fill(`${user}123`);
  await page.getByRole('button', { name: 'Přihlásit se' }).click();
  await expect(page).toHaveURL(
    area === 'skola' ? /\/skola\/home$/ : /\/superadmin$/,
  );
}

test('the support enters as a user the ticket names, both they and the ticket’s author see it, and switches to the author without leaving', async ({
  page,
  browser,
}) => {
  // Three logins.
  test.slow();
  const title = `E2E vstup jako ${Date.now()}`;

  // The ticket names its author, and the teacher the problem is about.
  await login(page, 'skola', 'admin');
  await page.goto('/skola/admin?group=app&section=support');
  await page.getByRole('button', { name: 'Nový požadavek' }).click();
  const create = page.getByRole('dialog', {
    name: 'Nový požadavek na podporu',
  });
  await create.getByLabel('Nadpis').fill(title);
  await create.getByLabel('Popis').fill('Učitelce nejde zapsat známka.');
  const asWhom = create.locator('p-multiselect');
  await expect(asWhom).toContainText('Alena Správcová');
  await asWhom.click();
  await expect(page.getByRole('option').first()).toContainText(
    'Já · Alena Správcová',
  );
  await page.getByRole('option', { name: /Jana Učitelová/ }).click();
  await page.keyboard.press('Escape');
  await create.getByRole('button', { name: 'Odeslat' }).click();
  await expect(page.getByText('Požadavek odeslán podpoře.')).toBeVisible();

  const superAdmin = await browser.newPage();
  await login(superAdmin, 'superadmin', 'superadmin');
  await superAdmin.goto('/superadmin/support');
  await superAdmin.getByRole('row').filter({ hasText: title }).click();
  const detail = superAdmin.getByRole('dialog', { name: title });
  await detail.getByRole('button', { name: 'Převzít' }).click();
  await expect(
    detail.getByText('Vstup jako: Alena Správcová, Jana Učitelová.'),
  ).toBeVisible();

  // The author first, a filter by name, the teacher in one of her roles.
  await detail.getByRole('button', { name: 'Vstoupit do školy' }).click();
  const picker = superAdmin.getByRole('dialog', { name: 'Vstoupit jako' });
  await expect(
    picker.getByTestId('support-as-users').locator('li').first(),
  ).toContainText('Alena Správcová');
  await picker.getByLabel('Jméno').fill('ucitelova');
  await expect(picker.getByText('Alena Správcová')).toHaveCount(0);
  const [school] = await Promise.all([
    superAdmin.context().waitForEvent('page'),
    picker.getByRole('button', { name: 'Jana Učitelová – Učitel' }).click(),
  ]);
  await expect(school).toHaveURL(/\/skola\/home$/);
  await school.bringToFront();
  await expect(school.getByTestId('support-banner')).toContainText(
    'jste přihlášen jako učitel Jana Učitelová',
  );

  // The teacher learns it, the author sees as whom.
  const teacher = await browser.newPage();
  await login(teacher, 'skola', 'ucitel');
  // Other tests' support may be in other accounts at the same time, a
  // repeated run's still in this one.
  const presence = teacher
    .getByTestId('support-presence')
    .filter({ hasText: 'superadmin superadmin se přihlásil k vašemu účtu' })
    .first();
  await presence.getByRole('button', { name: 'Detail', exact: true }).click();
  await expect(
    teacher.getByRole('dialog', { name: 'Podpora ve vašem účtu' }),
  ).toContainText('zůstane nepřečtené');
  await teacher.close();
  await page.bringToFront();
  await page.goto('/skola/home');
  await expect(
    page
      .getByTestId('support-presence')
      .filter({ hasText: 'je přihlášen jako učitel Jana Učitelová' })
      .first(),
  ).toBeVisible();

  // To the author within the same session.
  await school.bringToFront();
  await school.getByRole('button', { name: 'Přepnout…' }).click();
  await school
    .getByRole('dialog', { name: 'Přepnout na…' })
    .getByRole('button', { name: 'Alena Správcová – Admin' })
    .click();
  await expect(school.getByTestId('support-banner')).toContainText(
    'jste přihlášen jako admin Alena Správcová',
  );
  await school.getByRole('button', { name: 'Opustit' }).click();
  const closed = school.waitForEvent('close');
  await school
    .getByRole('dialog', { name: 'Opustit režim podpory' })
    .getByRole('button', { name: 'Opustit' })
    .click();
  await closed;
  await superAdmin.close();
});

import { expect, test } from './fixtures';

test('new school is created in a modal like other entities', async ({
  page,
}) => {
  await page.goto('/superadmin/login');
  await page.locator('#username').fill('superadmin');
  await page.locator('#password').fill('superadmin123');
  await page.getByRole('button', { name: 'Přihlásit se' }).click();
  await expect(page).toHaveURL(/\/superadmin$/);

  // No inline creation form on the page itself any more.
  await expect(page.locator('#schoolName')).toHaveCount(0);

  await page.getByRole('button', { name: 'Nová škola' }).click();
  const dialog = page.getByRole('dialog', { name: 'Nová škola' });
  await expect(dialog).toBeVisible();
  await expect(dialog.locator('#schoolName')).toBeVisible();
  await expect(
    dialog.getByRole('button', { name: 'Založit školu' }),
  ).toBeDisabled();
});

test('new SuperAdmin is added in a modal too', async ({ page }) => {
  await page.goto('/superadmin/login');
  await page.locator('#username').fill('superadmin');
  await page.locator('#password').fill('superadmin123');
  await page.getByRole('button', { name: 'Přihlásit se' }).click();
  await expect(page).toHaveURL(/\/superadmin$/);

  await expect(page.locator('#superAdminUsername')).toHaveCount(0);

  await page.getByRole('button', { name: 'Nový superadmin' }).click();
  const dialog = page.getByRole('dialog', { name: 'Nový superadmin' });
  await expect(dialog).toBeVisible();
  await expect(dialog.locator('#superAdminUsername')).toBeVisible();
  await expect(
    dialog.getByRole('button', { name: 'Přidat superadmina' }),
  ).toBeDisabled();
});

test('school is edited in the same modal', async ({ page }) => {
  await page.goto('/superadmin/login');
  await page.locator('#username').fill('superadmin');
  await page.locator('#password').fill('superadmin123');
  await page.getByRole('button', { name: 'Přihlásit se' }).click();
  await expect(page).toHaveURL(/\/superadmin$/);

  await page.getByRole('button', { name: 'Upravit školu' }).first().click();
  const dialog = page.getByRole('dialog', { name: 'Upravit školu' });
  await expect(dialog).toBeVisible();
  await expect(dialog.locator('#schoolName')).not.toHaveValue('');
  // The first admin is only set when creating a school.
  await expect(dialog.locator('#adminUsername')).toHaveCount(0);
  await expect(dialog.getByRole('button', { name: 'Uložit' })).toBeEnabled();
});

test('support mode: a ticket for the school, the school allows entering, the SuperAdmin enters, survives a reload, leaves', async ({
  page,
  browser,
}) => {
  // Two logins and the allowing in between.
  test.slow();
  const title = `E2E kontrola ${Date.now()}`;
  await page.goto('/superadmin/login');
  await page.locator('#username').fill('superadmin');
  await page.locator('#password').fill('superadmin123');
  await page.getByRole('button', { name: 'Přihlásit se' }).click();
  await expect(page).toHaveURL(/\/superadmin$/);

  // A plain SuperAdmin session never shows a school's pages.
  await page.goto('/skola/grades');
  await expect(page).toHaveURL(/\/superadmin$/);

  // Entering is for a ticket — the school's tickets, a new one for it.
  await page
    .getByRole('row', { name: /skola/ })
    .getByRole('button', { name: 'Požadavky školy' })
    .click();
  await expect(page).toHaveURL(/\/superadmin\/support$/);
  await page.getByRole('button', { name: 'Nový požadavek' }).click();
  const create = page.getByRole('dialog', {
    name: 'Nový požadavek pro školu',
  });
  await expect(create.getByLabel('Škola')).toContainText('Testovací škola');
  await create.getByLabel('Nadpis').fill(title);
  await create.getByLabel('Popis').fill('Kontrola **nastavení**');
  await create.getByRole('button', { name: 'Založit' }).click();
  const detail = page.getByRole('dialog', { name: title });
  await expect(detail.getByText('Řeší: superadmin')).toBeVisible();
  await expect(
    detail.locator('strong', { hasText: 'nastavení' }),
  ).toBeVisible();
  // Po schválení (the default): the school allows it first.
  await expect(
    detail.getByRole('button', { name: 'Vstoupit do školy' }),
  ).toHaveCount(0);
  await detail.getByRole('button', { name: 'Požádat o vstup' }).click();
  await expect(detail.getByText(/Podpora žádá o vstup/)).toBeVisible();

  const admin = await browser.newPage();
  await admin.goto('/skola/login');
  await admin.locator('#username').fill('admin');
  await admin.locator('#password').fill('admin123');
  await admin.getByRole('button', { name: 'Přihlásit se' }).click();
  await expect(admin).toHaveURL(/\/skola\/home$/);
  await admin.getByRole('button', { name: /^Upozornění/ }).click();
  await admin
    .getByText(`Podpora žádá o vstup do školy: ${title}`)
    .first()
    .click();
  const schoolDetail = admin.getByRole('dialog', { name: title });
  await schoolDetail.getByRole('button', { name: 'Povolit vstup' }).click();
  await expect(admin.getByText('Vstup povolen.')).toBeVisible();
  await admin.close();

  // Allowed — under the SuperAdmin's bell, leading back to the ticket.
  await detail.locator('.p-dialog-close-button').click();
  await expect(detail).toBeHidden();
  await page.getByRole('button', { name: /^Upozornění/ }).click();
  await page
    .getByText(`Škola povolila vstup (Testovací škola): ${title}`)
    .first()
    .click();
  await detail.getByRole('button', { name: 'Vstoupit do školy' }).click();
  await expect(page).toHaveURL(/\/skola\/home$/);
  await expect(page.getByText('Režim podpory')).toBeVisible();

  await page.reload();
  await expect(page).toHaveURL(/\/skola\/home$/);
  await expect(page.getByText('Režim podpory')).toBeVisible();

  await page.getByRole('button', { name: 'Opustit' }).click();
  const leave = page.getByRole('dialog', { name: 'Opustit režim podpory' });
  await leave.getByLabel('Výsledek práce').fill('E2E: nastavení zkontrolováno');
  await leave.getByRole('button', { name: 'Opustit' }).click();
  // Back at the ticket, the visit in its thread.
  await expect(page).toHaveURL(/\/superadmin\/support$/);
  await expect(page.getByRole('row').filter({ hasText: title })).toHaveClass(
    /app-highlight/,
  );
  await expect(detail.getByText('E2E: nastavení zkontrolováno')).toBeVisible();
  await expect(page.getByText('Režim podpory')).toHaveCount(0);
});

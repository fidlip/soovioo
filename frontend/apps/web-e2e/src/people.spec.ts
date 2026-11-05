import { Locator, Page } from '@playwright/test';
import { expect, test } from './fixtures';

// Osoby and Uživatelé list everyone's photo — the photos test deletes its
// own teacher's in parallel, a thumbnail already asked for then 404s.
test.use({ allowedConsoleMessages: [/status of 404/] });

/** Picks an option of a multiselect and closes its list — Escape only
 * once the list is open, otherwise it closes the dialog instead. */
async function pick(page: Page, field: Locator, option: string | RegExp) {
  await field.click();
  const list = page.getByRole('listbox');
  await expect(list).toBeVisible();
  await list
    .getByRole('option', { name: option, exact: typeof option === 'string' })
    .click();
  // On a busy runner the first Escape can come before the list listens.
  await expect(async () => {
    await page.keyboard.press('Escape');
    await expect(list).toBeHidden({ timeout: 1_000 });
  }).toPass();
}

test('an account is a person’s: picked, completed with the missing profile, then created', async ({
  page,
}) => {
  // Four dialogs across two tabs — over 30 s on a busy CI runner.
  test.slow();
  const lastName = `Osoba${Date.now()}`;
  await page.goto('/skola/login');
  await page.locator('#username').fill('admin');
  await page.locator('#password').fill('admin123');
  await page.getByRole('button', { name: 'Přihlásit se' }).click();
  await expect(page).toHaveURL(/\/skola\/home$/);
  await page.getByRole('link', { name: 'Administrace', exact: true }).click();

  // A teacher without an account, in Osoby.
  await page.getByRole('tab', { name: 'Osoby' }).click();
  const people = page.getByRole('tabpanel', { name: 'Osoby' });
  await people.getByRole('button', { name: 'Nová osoba' }).click();
  const personDialog = page.getByRole('dialog', { name: 'Nová osoba' });
  await personDialog.getByRole('textbox', { name: /^Jméno/ }).fill('Eva');
  await personDialog.getByRole('textbox', { name: /^Příjmení/ }).fill(lastName);
  await pick(
    page,
    personDialog.locator('p-multiselect[name="profiles"]'),
    'Učitel',
  );
  await personDialog.getByRole('button', { name: 'Přidat předmět' }).click();
  await personDialog
    .getByRole('combobox', { name: 'Aprobace 1 — předmět' })
    .click();
  await page.getByRole('option', { name: 'Matematika', exact: true }).click();
  await personDialog.getByRole('button', { name: 'Vytvořit osobu' }).click();
  await expect(personDialog).toBeHidden();
  const personRow = people.locator('tr', { hasText: lastName });
  await expect(personRow.getByText('Učitel (M)')).toBeVisible();
  await expect(personRow.getByText('bez účtu')).toBeVisible();

  // Her account as a teacher — she's offered — then also a parent, which
  // she has no profile for yet.
  await page.getByRole('tab', { name: 'Aplikace' }).click();
  await page.getByRole('tab', { name: 'Uživatelé' }).click();
  await page.getByRole('button', { name: 'Nový uživatel' }).click();
  const userDialog = page.getByRole('dialog', { name: 'Nový uživatel' });
  await pick(page, userDialog.locator('p-multiselect[name="roles"]'), 'Učitel');
  await userDialog.locator('#userPersonId').click();
  await page.getByRole('option', { name: `Eva ${lastName}` }).click();
  await pick(page, userDialog.locator('p-multiselect[name="roles"]'), 'Rodič');
  await expect(
    userDialog.getByText('Role Rodič potřebuje profil rodiče'),
  ).toBeVisible();
  await expect(
    userDialog.getByRole('button', { name: 'Vytvořit uživatele' }),
  ).toBeDisabled();

  await userDialog.getByRole('button', { name: 'Upravit osobu' }).click();
  const editDialog = page.getByRole('dialog', { name: 'Upravit osobu' });
  await pick(
    page,
    editDialog.locator('p-multiselect[name="profiles"]'),
    'Rodič',
  );
  // A child too — deleting her below takes the link along.
  await pick(
    page,
    editDialog.locator('p-multiselect[name="childIds"]'),
    /^Jan Novák/,
  );
  await editDialog.getByRole('button', { name: 'Uložit' }).click();
  await expect(editDialog).toBeHidden();
  await expect(
    userDialog.getByText('Role Rodič potřebuje profil rodiče'),
  ).toHaveCount(0);
  await expect(
    userDialog.getByRole('group', { name: `Osoba Eva ${lastName}` }),
  ).toContainText('Rodič:');

  await userDialog.locator('#password').fill('heslo123');
  await userDialog.getByRole('button', { name: 'Vytvořit uživatele' }).click();
  await expect(userDialog).toBeHidden();
  const userRow = page
    .getByRole('tabpanel', { name: 'Uživatelé' })
    .locator('tr', { hasText: lastName });
  await expect(userRow).toHaveCount(1);

  // Deleting the account leaves the person; then she goes too.
  await userRow.getByRole('button', { name: 'Smazat' }).click();
  await page
    .getByRole('alertdialog')
    .getByRole('button', { name: 'Smazat' })
    .click();
  await expect(userRow).toHaveCount(0);
  await page.getByRole('tab', { name: 'Škola' }).click();
  await page.getByRole('tab', { name: 'Osoby' }).click();
  await expect(personRow.getByText('bez účtu')).toBeVisible();
  await personRow.getByRole('button', { name: 'Smazat' }).click();
  await page
    .getByRole('alertdialog')
    .getByRole('button', { name: 'Smazat' })
    .click();
  await expect(personRow).toHaveCount(0);
});

test('what is typed stays when the dialog’s lists arrive late', async ({
  page,
  waitForApiIdle,
}) => {
  let release: () => void = () => undefined;
  const held = new Promise<void>((resolve) => (release = resolve));
  await page.route('**/api/countries', async (route) => {
    await held;
    await route.continue();
  });
  await page.goto('/skola/login');
  await page.locator('#username').fill('admin');
  await page.locator('#password').fill('admin123');
  await page.getByRole('button', { name: 'Přihlásit se' }).click();
  await expect(page).toHaveURL(/\/skola\/home$/);
  await page.getByRole('link', { name: 'Administrace', exact: true }).click();
  await page.getByRole('tab', { name: 'Osoby' }).click();
  const requested = page.waitForRequest('**/api/countries');
  await page.getByRole('button', { name: 'Nová osoba' }).click();
  await requested;
  const dialog = page.getByRole('dialog', { name: 'Nová osoba' });
  const lastName = dialog.getByRole('textbox', { name: /^Příjmení/ });
  await lastName.fill('Pozdní');

  release();
  await waitForApiIdle();
  await expect(lastName).toHaveValue('Pozdní');
});

test('Osoby are filtered by the school’s roles of their accounts', async ({
  page,
}) => {
  await page.goto('/skola/login');
  await page.locator('#username').fill('admin');
  await page.locator('#password').fill('admin123');
  await page.getByRole('button', { name: 'Přihlásit se' }).click();
  await expect(page).toHaveURL(/\/skola\/home$/);
  await page.getByRole('link', { name: 'Administrace', exact: true }).click();
  await page.getByRole('tab', { name: 'Osoby' }).click();
  const people = page.getByRole('tabpanel', { name: 'Osoby' });
  const parent = people.locator('tr', { hasText: 'Petr Novák' });
  const teacher = people.locator('tr', { hasText: 'Jana Učitelová' });
  await expect(teacher).toHaveCount(1);

  await people.getByRole('combobox', { name: 'Role' }).click();
  await page.getByRole('option', { name: 'Rodič', exact: true }).click();
  await expect(teacher).toHaveCount(0);
  await expect(parent).toHaveCount(1);

  // Combined with Kdo: Petr Novák has no teacher's profile.
  await people.getByRole('combobox', { name: 'Kdo' }).click();
  await page.getByRole('option', { name: 'Učitelé', exact: true }).click();
  await expect(parent).toHaveCount(0);
});

test('Osoby: filters of a teacher and a parent appear with Kdo, Třída finds everyone of the class, filters clear', async ({
  page,
}) => {
  // Lessons of other tests come and go in parallel: she teaches just what she leads.
  await page.route('**/api/people', async (route) => {
    const response = await route.fetch();
    const all: {
      name: string;
      teacher: {
        classTeacherOfIds: number[];
        teachesClassIds: number[];
      } | null;
    }[] = await response.json();
    for (const person of all) {
      if (person.teacher) {
        person.teacher.teachesClassIds =
          person.name === 'Jana Učitelová'
            ? person.teacher.classTeacherOfIds
            : [];
      }
    }
    await route.fulfill({ response, json: all });
  });
  await page.goto('/skola/login');
  await page.locator('#username').fill('admin');
  await page.locator('#password').fill('admin123');
  await page.getByRole('button', { name: 'Přihlásit se' }).click();
  await expect(page).toHaveURL(/\/skola\/home$/);
  await page.getByRole('link', { name: 'Administrace', exact: true }).click();
  await page.getByRole('tab', { name: 'Osoby' }).click();
  const people = page.getByRole('tabpanel', { name: 'Osoby' });
  const parent = people.locator('tr', { hasText: 'Petr Novák' });
  const teacher = people.locator('tr', { hasText: 'Jana Učitelová' });
  const pupil = people.locator('tr', { hasText: 'Jan Novák' });
  const select = async (label: string, option: string) => {
    await people.getByRole('combobox', { name: label, exact: true }).click();
    await page.getByRole('option', { name: option, exact: true }).click();
  };
  const clear = people.getByRole('button', { name: 'Zrušit filtry' });
  await expect(teacher).toHaveCount(1);
  await expect(clear).toHaveCount(0);

  // The demo's Jana Učitelová leads 5.A, Petr Novák is the father of its Jan.
  await select('Třída', '5.A');
  await expect(teacher).toHaveCount(1);
  await expect(parent).toHaveCount(1);
  await expect(pupil).toHaveCount(1);
  await clear.click();
  await expect(clear).toHaveCount(0);
  await expect(
    people.getByRole('combobox', { name: 'Třída', exact: true }),
  ).toHaveText(/^\s*$/);

  await expect(people.getByLabel('Dítě')).toHaveCount(0);
  await expect(
    people.getByRole('combobox', { name: 'Třídní', exact: true }),
  ).toHaveCount(0);

  await select('Kdo', 'Učitelé');
  await select('Třídní', '5.A');
  await expect(teacher).toHaveCount(1);
  await expect(people.locator('tbody tr')).toHaveCount(1);
  await select('Učí ve třídě', '5.A');
  await expect(teacher).toHaveCount(1);
  await people.getByRole('combobox', { name: 'Aprobace', exact: true }).click();
  await expect(page.getByRole('option').first()).toBeVisible();
  await expect(page.getByRole('option', { name: 'Učitelé' })).toHaveCount(0);
  const subject = page.getByRole('option', { name: /Matematika/ });
  await subject.click();
  await expect(teacher).toHaveCount(1);

  // Another kind's filters go with their fields.
  await select('Kdo', 'Rodiče');
  await expect(
    people.getByRole('combobox', { name: 'Třídní', exact: true }),
  ).toHaveCount(0);
  await expect(parent).toHaveCount(1);
  await people.getByLabel('Dítě').fill('jan novak');
  await expect(parent).toHaveCount(1);
  await people.getByLabel('Dítě').fill('nikdo takovy');
  await expect(parent).toHaveCount(0);

  await clear.click();
  await expect(people.getByLabel('Dítě')).toHaveCount(0);
  await expect(teacher).toHaveCount(1);
});

test('a class teacher’s Rodiče are filtered by name and child', async ({
  page,
}) => {
  await page.goto('/skola/login');
  await page.locator('#username').fill('ucitel');
  await page.locator('#password').fill('ucitel123');
  await page.getByRole('button', { name: 'Přihlásit se' }).click();
  await expect(page).toHaveURL(/\/skola\/home$/);
  await page.getByRole('link', { name: 'Administrace', exact: true }).click();
  await page.getByRole('tab', { name: 'Rodiče' }).click();
  const parent = page.getByRole('cell', { name: 'Petr Novák' });
  await expect(parent).toBeVisible();

  await page.getByLabel('Dítě').fill('nikdo takovy');
  await expect(parent).toHaveCount(0);
  await page.getByRole('button', { name: 'Zrušit filtry' }).click();
  await expect(parent).toBeVisible();
  await expect(page.getByLabel('Dítě')).toHaveValue('');

  await page.locator('#filterParentName').fill('petr novak');
  await expect(parent).toBeVisible();
  await page.getByRole('button', { name: 'Zrušit filtry' }).click();

  await page.getByLabel('Kontakt').fill('neexistujici@example.com');
  await expect(parent).toHaveCount(0);
});

for (const { link, field, option } of [
  { link: 'Docházka', field: 'Stav', option: 'Omluveno' },
  { link: 'Známky', field: 'Předmět', option: null },
  { link: 'Domácí úkoly', field: 'Termín', option: 'Archiv' },
  { link: 'Platby', field: 'Stav', option: 'Zaplaceno' },
]) {
  test(`${link}: a filter is cleared with Zrušit filtry`, async ({ page }) => {
    await page.goto('/skola/login');
    await page.locator('#username').fill('admin');
    await page.locator('#password').fill('admin123');
    await page.getByRole('button', { name: 'Přihlásit se' }).click();
    await expect(page).toHaveURL(/\/skola\/home$/);
    await page.getByRole('link', { name: link, exact: true }).click();
    const clear = page.getByRole('button', { name: 'Zrušit filtry' });
    await expect(clear).toHaveCount(0);
    const combobox = page.getByRole('combobox', { name: field, exact: true });
    const before = await combobox.textContent();

    await combobox.click();
    const choice = option
      ? page.getByRole('option', { name: option, exact: true })
      : page.getByRole('option').first();
    await choice.click();
    await expect(clear).toBeVisible();

    await clear.click();
    await expect(clear).toHaveCount(0);
    await expect(combobox).toHaveText(before ?? '');
  });
}

test('Zprávy: clearing the filter shows all conversations', async ({
  page,
}) => {
  await page.goto('/skola/login');
  await page.locator('#username').fill('admin');
  await page.locator('#password').fill('admin123');
  await page.getByRole('button', { name: 'Přihlásit se' }).click();
  await expect(page).toHaveURL(/\/skola\/home$/);
  await page
    .getByRole('navigation', { name: 'Hlavní navigace' })
    .getByRole('link', { name: 'Zprávy', exact: true })
    .click();
  const clear = page.getByRole('button', { name: 'Zrušit filtry' });
  await clear.click();
  await expect(clear).toHaveCount(0);
  await expect(
    page.getByRole('combobox', { name: 'Stav', exact: true }),
  ).toHaveText('Vše');
});

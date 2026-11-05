import { Page } from '@playwright/test';
import { expect, test } from './fixtures';

async function login(page: Page) {
  await page.goto('/skola/login');
  await page.locator('#username').fill('admin');
  await page.locator('#password').fill('admin123');
  await page.getByRole('button', { name: 'Přihlásit se' }).click();
  await expect(page).toHaveURL(/\/skola\/home$/);
}

function openNav(page: Page, name: string) {
  return page
    .getByRole('navigation', { name: 'Hlavní navigace' })
    .getByRole('link', { name, exact: true })
    .click();
}

async function pick(
  page: Page,
  combobox: ReturnType<Page['getByRole']>,
  option: string,
) {
  await combobox.click();
  const list = page.locator(`#${await combobox.getAttribute('aria-controls')}`);
  await list.getByRole('option', { name: option }).click();
}

function inDays(days: number): string {
  const date = new Date(Date.now() + days * 86_400_000);
  return `${date.getDate()}.${date.getMonth() + 1}.${date.getFullYear()}`;
}

async function setProgram(page: Page, program: string | null) {
  await openNav(page, 'Administrace');
  await page.getByRole('tab', { name: 'Třídy' }).click();
  await page
    .getByRole('row')
    .filter({ hasText: '5.A' })
    .getByRole('button', { name: 'Upravit' })
    .click();
  const dialog = page.getByRole('dialog', { name: 'Upravit třídu' });
  const select = dialog.getByRole('combobox', {
    name: 'Vzdělávací program (RVP)',
  });
  if (program) {
    await pick(page, select, program);
  } else {
    await dialog
      .locator('p-select[name="classRvpProgram"] .p-select-clear-icon')
      .click();
  }
  await dialog.getByRole('button', { name: 'Uložit' }).click();
  await expect(dialog).toBeHidden();
}

const REVISED = 'Základní vzdělávání (revidovaný RVP ZV)';

// A draft far ahead gets its plan filled by 5.A's program and is archived
// again, 5.A loses the program — so the shared data end as they started.
test.describe.serial('RVP', () => {
  const name = `E2E RVP ${Date.now()}`;

  test('a základní škola is offered only its programs', async ({ page }) => {
    await login(page);
    await openNav(page, 'Administrace');
    await page.getByRole('tab', { name: 'Třídy' }).click();
    await page
      .getByRole('row')
      .filter({ hasText: '5.A' })
      .getByRole('button', { name: 'Upravit' })
      .click();
    const select = page
      .getByRole('dialog', { name: 'Upravit třídu' })
      .getByRole('combobox', { name: 'Vzdělávací program (RVP)' });
    await select.click();
    const list = page.locator(`#${await select.getAttribute('aria-controls')}`);
    await expect(list.getByRole('option')).toHaveText([
      'Základní vzdělávání (dosavadní RVP ZV)',
      'Základní vzdělávání (revidovaný RVP ZV)',
    ]);
  });

  test('a draft of a class with a program is filled and checked', async ({
    page,
  }) => {
    await login(page);
    await setProgram(page, REVISED);

    await openNav(page, 'Rozvrh');
    await page.getByRole('button', { name: 'Upravit rozvrh' }).click();
    await page.getByRole('button', { name: 'Verze rozvrhu' }).click();
    const versions = page.getByRole('dialog', { name: 'Verze rozvrhu' });
    await versions.getByRole('button', { name: 'Nový koncept' }).click();
    await versions.getByLabel('Název').fill(name);
    await versions
      .getByLabel('Platí od')
      .pressSequentially(inDays(800 + Math.floor(Math.random() * 300)));
    await versions.getByRole('button', { name: 'Vytvořit koncept' }).click();
    await expect(versions).toBeHidden();

    await openNav(page, 'Administrace');
    await page.getByRole('tab', { name: 'Výuka' }).click();
    await pick(
      page,
      page.getByRole('combobox', { name: 'Verze rozvrhu' }),
      name,
    );
    await page.getByRole('tab', { name: 'Učební plán' }).click();
    const panel = page.getByRole('region', { name: 'Kontrola podle RVP' });
    await expect(panel).toContainText(REVISED);

    await panel.getByRole('button', { name: 'Vyplnit podle RVP' }).click();
    const fill = page.getByRole('dialog', {
      name: 'Vyplnit učební plán podle RVP',
    });
    const status = fill.getByRole('status');
    await expect(status).toContainText('Přibude');

    // Each option tells what it'd do before anything is saved.
    await fill.getByText('Přepsat hodiny, které už v plánu jsou').click();
    await expect(status).toContainText('přepíše se');
    await fill.getByText('Založit předměty, které škola nemá').click();
    await expect(status).toContainText('Bez předmětu se vynechá');
    await fill.getByRole('button', { name: /Vyplnit$/ }).click();
    await expect(fill).toBeHidden();
    await expect(page.getByText('Učební plán je vyplněný.')).toBeVisible();

    await expect(
      page.getByRole('spinbutton', { name: 'Matematika — 5. ročník' }),
    ).toHaveValue('5');
    await expect(panel).toContainText('chybí ročníky');
  });

  test('a published version offers no filling and the draft is archived', async ({
    page,
  }) => {
    await login(page);
    await openNav(page, 'Administrace');
    await page.getByRole('tab', { name: 'Výuka' }).click();
    await page.getByRole('tab', { name: 'Učební plán' }).click();
    await expect(
      page.getByRole('region', { name: 'Kontrola podle RVP' }),
    ).toContainText(REVISED);
    await expect(
      page.getByRole('button', { name: 'Vyplnit podle RVP' }),
    ).toHaveCount(0);

    await openNav(page, 'Rozvrh');
    await page.getByRole('button', { name: 'Upravit rozvrh' }).click();
    await page.getByRole('button', { name: 'Verze rozvrhu' }).click();
    const version = page
      .getByRole('dialog', { name: 'Verze rozvrhu' })
      .getByTestId('timetable-version')
      .filter({ hasText: name });
    await version.getByRole('button', { name: 'Archivovat' }).click();
    await page
      .getByRole('alertdialog', { name: 'Archivovat verzi rozvrhu' })
      .getByRole('button', { name: 'Archivovat' })
      .click();
    await expect(version).toContainText('Archivovaná');
    await page.reload();

    await setProgram(page, null);
  });
});

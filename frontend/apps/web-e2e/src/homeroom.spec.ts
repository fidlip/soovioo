import { Page } from '@playwright/test';
import { expect, test } from './fixtures';
import {
  discard,
  draftCopy,
  showVersion,
  versionsApi,
} from './timetable-versions';

async function pick(page: Page, name: string, option: string) {
  await page.getByRole('combobox', { name }).click();
  await page.getByRole('option', { name: option, exact: true }).click();
}

test('a class’s homeroom is set in Administrace and fills in a new lesson', async ({
  page,
  request,
}) => {
  const room = `E2E-${Date.now()}`;
  const className = `H${Date.now() % 100000}`;

  await page.goto('/skola/login');
  await page.locator('#username').fill('admin');
  await page.locator('#password').fill('admin123');
  await page.getByRole('button', { name: 'Přihlásit se' }).click();
  await expect(page).toHaveURL(/\/skola\/home$/);
  const navigation = page.getByRole('navigation', { name: 'Hlavní navigace' });
  await navigation
    .getByRole('link', { name: 'Administrace', exact: true })
    .click();

  await page.getByRole('tab', { name: 'Učebny' }).click();
  await page.getByRole('button', { name: 'Nová učebna' }).click();
  const roomDialog = page.getByRole('dialog', { name: 'Nová učebna' });
  await roomDialog.getByLabel('Název', { exact: true }).fill(room);
  await pick(page, 'Typ', 'Kmenová');
  await roomDialog.locator('button[type="submit"]').click();
  await expect(roomDialog).toBeHidden();

  await page.getByRole('tab', { name: 'Třídy' }).click();
  await page.getByRole('button', { name: 'Nová třída' }).click();
  const classDialog = page.getByRole('dialog', { name: 'Nová třída' });
  await classDialog.getByLabel('Název třídy', { exact: true }).fill(className);
  await pick(page, 'Kmenová učebna', room);
  await classDialog.locator('button[type="submit"]').click();
  await expect(classDialog).toBeHidden();
  const classRow = page.getByRole('row').filter({ hasText: className });
  await expect(classRow).toContainText(room);

  const admin = await versionsApi(page, request);
  const version = `E2E kmenová ${Date.now()}`;
  const versionId = await draftCopy(admin, version, 4000);
  await navigation.getByRole('link', { name: 'Rozvrh', exact: true }).click();
  await page.locator('p-select[name="viewMode"]').click();
  await page.getByRole('option', { name: 'Třída', exact: true }).click();
  await page.locator('p-select[name="classPicker"]').click();
  await page.getByRole('option', { name: className, exact: true }).click();
  await page.getByRole('button', { name: 'Upravit rozvrh' }).click();
  await showVersion(page, version);
  const add = page.getByRole('button', { name: 'Přidat hodinu' }).first();
  await add.locator('xpath=ancestor::td').hover();
  await add.click();
  const lesson = page.getByRole('dialog', { name: 'Nová hodina' });
  await pick(page, 'Předmět', 'Matematika');
  await expect(lesson.getByRole('combobox', { name: 'Učebna' })).toHaveText(
    room,
  );
  await lesson.getByRole('button', { name: 'Zrušit' }).click();
  await discard(admin, versionId);

  await navigation
    .getByRole('link', { name: 'Administrace', exact: true })
    .click();
  await page.getByRole('tab', { name: 'Třídy' }).click();
  await classRow.getByRole('button', { name: 'Smazat' }).click();
  await expect(classRow).toHaveCount(0);
  await page.getByRole('tab', { name: 'Učebny' }).click();
  await page
    .getByRole('row')
    .filter({ hasText: room })
    .getByRole('button', { name: 'Smazat' })
    .click();
  await expect(page.getByRole('row').filter({ hasText: room })).toHaveCount(0);
});

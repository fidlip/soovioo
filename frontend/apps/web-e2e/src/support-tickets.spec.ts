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

test('a school asks for support allowing entering; the assignee works on it in the thread, the school takes the entering back, the ticket is released, taken again and resolved', async ({
  page,
  browser,
}) => {
  // Two logins and back.
  test.slow();
  const title = `E2E požadavek ${Date.now()}`;

  await login(page, 'skola', 'admin');
  await page.goto('/skola/admin?group=app&section=support');
  await page.getByRole('button', { name: 'Nový požadavek' }).click();
  const create = page.getByRole('dialog', {
    name: 'Nový požadavek na podporu',
  });
  await create.getByLabel('Nadpis').fill(title);
  await create.getByLabel('Popis').fill('Prosíme o kontrolu _rozvrhu_.');
  await create.getByRole('tab', { name: 'Náhled' }).click();
  await expect(create.locator('em', { hasText: 'rozvrhu' })).toBeVisible();
  await expect(create.getByLabel(/Povolit podpoře vstup/)).toBeChecked();
  await create.getByRole('button', { name: 'Odeslat' }).click();
  await expect(page.getByText('Požadavek odeslán podpoře.')).toBeVisible();
  const schoolRow = page.getByRole('row').filter({ hasText: title });
  await expect(schoolRow.getByText('Nový')).toBeVisible();
  await expect(schoolRow.getByText(/Vstup podpory povolen do/)).toBeVisible();

  const superAdmin = await browser.newPage();
  await login(superAdmin, 'superadmin', 'superadmin');
  // The new ticket under the SuperAdmin's bell opens it.
  await superAdmin.getByRole('button', { name: /^Upozornění/ }).click();
  await superAdmin
    .getByText(`Nový požadavek (Testovací škola): ${title}`)
    .click();
  await expect(superAdmin).toHaveURL(/\/superadmin\/support$/);
  const row = superAdmin.getByRole('row').filter({ hasText: title });
  await expect(row).toHaveClass(/app-highlight/);
  const detail = superAdmin.getByRole('dialog', { name: title });
  await expect(detail.getByText('Řeší: zatím nikdo')).toBeVisible();
  // Only whoever takes care of it enters.
  await expect(
    detail.getByRole('button', { name: 'Vstoupit do školy' }),
  ).toHaveCount(0);
  await detail.getByRole('button', { name: 'Převzít' }).click();
  await expect(detail.getByText('Řeší: superadmin')).toBeVisible();

  // The school takes the entering back while the support is in: out at once.
  await detail.getByRole('button', { name: 'Vstoupit do školy' }).click();
  await expect(superAdmin).toHaveURL(/\/skola\/home$/);
  await page.reload();
  await page.getByRole('row').filter({ hasText: title }).click();
  const schoolDetail = page.getByRole('dialog', { name: title });
  await expect(schoolDetail.getByText('Stav: Řeší se')).toBeVisible();
  await schoolDetail.getByRole('button', { name: 'Odebrat povolení' }).click();
  await expect(page.getByText('Povolení odebráno.')).toBeVisible();
  await superAdmin.getByRole('link', { name: 'Domácí úkoly' }).click();
  await expect(superAdmin).toHaveURL(/\/superadmin\/support$/);
  await expect(superAdmin.getByText('Režim podpory')).toHaveCount(0);
  await expect(detail.getByText('Povolení vstupu odebráno')).toBeVisible();

  // An internal note and a question for the school.
  await detail.getByText('Interní poznámka (škola ji neuvidí)').click();
  await detail
    .getByLabel('Interní poznámka', { exact: true })
    .fill('Asi chybí úvazky');
  await detail.getByRole('button', { name: 'Uložit poznámku' }).click();
  await expect(detail.getByText('Asi chybí úvazky')).toBeVisible();
  await detail.getByText('Interní poznámka (škola ji neuvidí)').click();
  await detail.getByLabel('Zpráva škole').fill('Máte zadané úvazky?');
  await detail.getByRole('button', { name: 'Odeslat' }).click();
  await expect(detail.getByText('Máte zadané úvazky?')).toBeVisible();
  await detail.getByLabel('Stav').click();
  await superAdmin.getByRole('option', { name: 'Čeká na školu' }).click();
  await expect(superAdmin.getByText('Stav změněn.')).toBeVisible();

  // The school answers — not seeing the note — and it's back with the support.
  await page.reload();
  await page.getByRole('row').filter({ hasText: title }).click();
  await expect(schoolDetail.getByText('Máte zadané úvazky?')).toBeVisible();
  await expect(schoolDetail.getByText('Asi chybí úvazky')).toHaveCount(0);
  await schoolDetail.getByLabel('Zpráva podpoře').fill('Ano, zadané.');
  await schoolDetail.getByRole('button', { name: 'Odeslat' }).click();
  await expect(schoolDetail.getByText('Ano, zadané.')).toBeVisible();
  await expect(schoolDetail.getByText('Přiřazeno').first()).toBeVisible();

  // Released with a summary, taken again, resolved.
  await superAdmin.reload();
  await row.click();
  await expect(detail.getByText('Ano, zadané.')).toBeVisible();
  await detail.getByRole('button', { name: 'Přerušit práci' }).click();
  const release = superAdmin.getByRole('dialog', { name: 'Přerušit práci' });
  await release
    .getByLabel('Shrnutí dosavadní práce')
    .fill('Úvazky jsou, problém je jinde.');
  await release.getByRole('button', { name: 'Přerušit práci' }).click();
  await expect(detail.getByText('Řeší: zatím nikdo')).toBeVisible();
  await expect(
    detail.getByText('Úvazky jsou, problém je jinde.'),
  ).toBeVisible();
  await detail.getByRole('button', { name: 'Převzít' }).click();
  await detail.getByLabel('Stav').click();
  await superAdmin.getByRole('option', { name: 'Vyřešeno' }).click();
  await expect(detail.getByText('Vyřešeno').first()).toBeVisible();
  await superAdmin.keyboard.press('Escape');
  await superAdmin.reload();
  // Resolved ones leave the open list.
  await expect(row).toHaveCount(0);
  await superAdmin.close();

  await page.reload();
  await expect(
    page.getByRole('row').filter({ hasText: title }).getByText('Vyřešeno'),
  ).toBeVisible();
});

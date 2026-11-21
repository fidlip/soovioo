import { Locator, Page } from '@playwright/test';
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
  // As the ticket's author; the school opens in a new tab, this one stays
  // at the ticket.
  await detail.getByRole('button', { name: 'Vstoupit do školy' }).click();
  const [school] = await Promise.all([
    superAdmin.context().waitForEvent('page'),
    superAdmin
      .getByRole('dialog', { name: 'Vstoupit jako' })
      .getByRole('button', { name: 'Alena Správcová – Admin' })
      .click(),
  ]);
  await expect(school).toHaveURL(/\/skola\/home$/);
  // Writing to the ticket from inside the school. The strip's buttons come
  // when the page is idle — not while it's in the background.
  await school.bringToFront();
  await school
    .getByRole('button', { name: 'Detail požadavku', exact: true })
    .click();
  const inSchool = school.getByRole('dialog', { name: title });
  await inSchool.getByLabel('Zpráva škole').fill('Dívám se na rozvrh.');
  await inSchool.getByRole('button', { name: 'Odeslat' }).click();
  await expect(inSchool.getByText('Dívám se na rozvrh.')).toBeVisible();
  await school.keyboard.press('Escape');
  await page.reload();
  await page.getByRole('row').filter({ hasText: title }).click();
  const schoolDetail = page.getByRole('dialog', { name: title });
  await expect(schoolDetail.getByText('Stav: Řeší se')).toBeVisible();
  await schoolDetail.getByRole('button', { name: /Odebrat$/ }).click();
  await expect(page.getByText('Povolení odebráno.')).toBeVisible();
  // The visit is one item of the thread, the last one unfolded: ended by
  // taking the entering back, with what happened meanwhile.
  const visit = schoolDetail.locator('[data-kind="VisitGroup"]');
  await expect(visit.getByText('· ukončen')).toBeVisible();
  await expect(visit.getByText('Stav: Řeší se')).toBeVisible();
  // Folded, it keeps the school's decision, not the state entering set.
  await visit.getByRole('button', { name: /Vstup do školy/ }).click();
  await expect(visit.getByText('Stav: Řeší se')).toHaveCount(0);
  await expect(visit.getByText('Povolení vstupu odebráno')).toBeVisible();
  await expect(visit.getByText('Dívám se na rozvrh.')).toBeVisible();
  await expect(schoolDetail.getByTestId('ticket-visits-total')).toBeVisible();
  // Out at once: the school's tab says by itself who ended it and why, and
  // closes once that's confirmed.
  const ended = school.getByRole('dialog', { name: 'Vstup do školy ukončen' });
  await expect(
    ended.getByText('Škola odebrala povolení vstupu.'),
  ).toBeVisible();
  const closed = school.waitForEvent('close');
  // The click closes the tab: Playwright may report it gone before the
  // click itself returns.
  await ended
    .getByRole('button', { name: 'Rozumím' })
    .click()
    .catch((error) => {
      if (!school.isClosed()) {
        throw error;
      }
    });
  await closed;
  await superAdmin.bringToFront();
  await expect(detail.getByText('Povolení vstupu odebráno')).toBeVisible({
    timeout: 20_000,
  });

  // An internal note and a question for the school.
  await detail.getByRole('switch', { name: /Interní/ }).click();
  await detail
    .getByLabel('Interní poznámka', { exact: true })
    .fill('Asi chybí úvazky');
  await detail.getByRole('button', { name: 'Uložit poznámku' }).click();
  await expect(detail.getByText('Asi chybí úvazky')).toBeVisible();
  await detail.getByRole('switch', { name: /Interní/ }).click();
  await detail.getByLabel('Zpráva škole').fill('Máte zadané úvazky?');
  await detail.getByLabel('Zpráva škole').press('Control+Enter');
  await expect(detail.getByText('Máte zadané úvazky?')).toBeVisible();
  // The note is corrected; its change shows under "upraveno".
  const entryAction = async (entry: Locator, action: string) => {
    // Scrolling closes the menu — so not in the same frame as the click.
    const more = entry.getByRole('button', { name: 'Upravit, smazat…' });
    await more.scrollIntoViewIfNeeded();
    await more.click();
    await superAdmin.getByRole('menuitem', { name: action }).click();
  };
  const note = detail.locator('[data-kind="Note"]').first();
  await entryAction(note, 'Upravit');
  await note.getByLabel('Interní poznámka').fill('Asi chybí úvazky učitelů');
  await note.getByRole('button', { name: 'Uložit' }).click();
  await expect(note.getByText('Asi chybí úvazky učitelů')).toBeVisible();
  await note.getByRole('button', { name: 'upraveno' }).click();
  await expect(
    note.getByRole('list', { name: 'Změny' }).getByText('Asi chybí úvazky', {
      exact: true,
    }),
  ).toBeVisible();
  // A message made a note and sent again.
  const asked = (kind: string) =>
    detail
      .locator(`[data-kind="${kind}"]`)
      .filter({ hasText: 'Máte zadané úvazky?' });
  await entryAction(asked('Message'), 'Změnit na interní');
  await superAdmin
    .getByRole('alertdialog')
    .getByRole('button', { name: 'Změnit na interní' })
    .click();
  await expect(asked('Note')).toBeVisible();
  await entryAction(asked('Note'), 'Poslat škole');
  await superAdmin
    .getByRole('alertdialog')
    .getByRole('button', { name: 'Poslat škole' })
    .click();
  await expect(asked('Message')).toBeVisible();
  await asked('Message').getByRole('button', { name: 'upraveno' }).click();
  await expect(
    asked('Message').getByText(/změněno na interní poznámku/),
  ).toBeVisible();
  await expect(asked('Message').getByText(/posláno škole/)).toBeVisible();
  await detail.getByRole('button', { name: 'Čekám na školu' }).click();
  await expect(superAdmin.getByText('Stav změněn.')).toBeVisible();

  // The school answers — not seeing the note — and it's back with the support.
  await page.reload();
  await page.getByRole('row').filter({ hasText: title }).click();
  await expect(schoolDetail.getByText('Máte zadané úvazky?')).toBeVisible();
  await expect(schoolDetail.getByText('Asi chybí úvazky')).toHaveCount(0);
  await schoolDetail.getByLabel('Zpráva podpoře').fill('Ano, zadané.');
  await schoolDetail.getByRole('button', { name: 'Odeslat' }).click();
  await expect(schoolDetail.getByText('Ano, zadané.')).toBeVisible();
  // The school corrects its answer.
  const answer = schoolDetail.locator('[data-kind="Message"]').last();
  await answer.getByRole('button', { name: 'Upravit, smazat…' }).click();
  await page.getByRole('menuitem', { name: 'Upravit' }).click();
  await answer.getByLabel('Zpráva').fill('Ano, všechny zadané.');
  await answer.getByRole('button', { name: 'Uložit' }).click();
  await expect(schoolDetail.getByText('Ano, všechny zadané.')).toBeVisible();
  await expect(schoolDetail.getByText('Přiřazeno').first()).toBeVisible();

  // Released with a summary, taken again, resolved.
  await superAdmin.reload();
  await row.click();
  await expect(detail.getByText('Ano, všechny zadané.')).toBeVisible();
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
  // The school allows entering again — the open detail shows it by itself.
  await page.reload();
  await page.getByRole('row').filter({ hasText: title }).click();
  await schoolDetail.getByRole('button', { name: 'Povolit vstup' }).click();
  await expect(page.getByText('Vstup povolen.')).toBeVisible();
  await expect(
    detail.getByRole('button', { name: 'Vstoupit do školy' }),
  ).toBeVisible({ timeout: 25_000 });
  await detail.getByRole('button', { name: 'Vyřešeno' }).click();
  await superAdmin
    .getByRole('alertdialog')
    .getByRole('button', { name: 'Vyřešeno' })
    .click();
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

test('a school cancels a ticket only saying why; the support sees the reason', async ({
  page,
}) => {
  const title = `E2E zrušený ${Date.now()}`;
  await login(page, 'skola', 'admin');
  await page.goto('/skola/admin?group=app&section=support');
  // Set in Údaje o škole, shown here.
  await expect(page.getByTestId('support-access')).toContainText(
    'Po schválení',
  );
  await page.getByRole('button', { name: 'Nový požadavek' }).click();
  const create = page.getByRole('dialog', {
    name: 'Nový požadavek na podporu',
  });
  await create.getByLabel('Nadpis').fill(title);
  await create.getByRole('button', { name: 'Odeslat' }).click();
  await expect(page.getByText('Požadavek odeslán podpoře.')).toBeVisible();
  await page.getByRole('row').filter({ hasText: title }).click();
  const detail = page.getByRole('dialog', { name: title });
  await detail.getByRole('button', { name: 'Zrušit požadavek' }).click();
  const cancel = page.getByRole('dialog', { name: 'Zrušit požadavek' });
  await expect(
    cancel.getByRole('button', { name: 'Zrušit požadavek' }),
  ).toBeDisabled();
  // Thought better of it: the ticket stays.
  await cancel.getByRole('button', { name: 'Zpět' }).click();
  await expect(detail.getByText('Nový').first()).toBeVisible();

  await detail.getByRole('button', { name: 'Zrušit požadavek' }).click();
  await cancel.getByLabel('Důvod zrušení').fill('Vyřešili jsme to sami.');
  await cancel.getByRole('button', { name: 'Zrušit požadavek' }).click();
  await expect(page.getByText('Požadavek zrušen.')).toBeVisible();
  await expect(detail.getByText('Stav: Zrušeno')).toBeVisible();
  await expect(detail.getByText('Vyřešili jsme to sami.')).toBeVisible();
});

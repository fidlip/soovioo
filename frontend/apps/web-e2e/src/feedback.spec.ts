import { Page } from '@playwright/test';
import { expect, test } from './fixtures';

/** The dev API runs 880 ports above the web dev server (see devApiPort). */
function apiUrl(page: Page): string {
  const url = new URL(page.url());
  return `http://${url.hostname}:${Number(url.port || 4200) + 880}/api`;
}

async function badgeCount(page: Page): Promise<number> {
  const badge = page
    .getByRole('link', { name: 'Zpětná vazba' })
    .locator('.app-nav-badge');
  return (await badge.count()) === 0 ? 0 : Number(await badge.textContent());
}

/**
 * A teacher sends feedback from Nastavení. SuperAdmin → Zpětná vazba: it
 * and a demo visitor's (the demo deleted since, the consented e-mail kept)
 * are new until opened, can be flagged, filtered, marked unseen again and
 * deleted. Replying needs an SMTP server — covered by the API tests. The
 * badge counts all unseen feedback, so no other test may create any.
 */
test('feedback: sent from Nastavení and a demo, new until opened, flagged, filtered, deleted', async ({
  page,
  request,
  browser,
}) => {
  test.slow();
  const schoolText = `Chci export do Bakalářů ${Date.now()}`;
  const teacher = await browser.newPage();
  try {
    await teacher.goto('/skola/login');
    await teacher.locator('#username').fill('ucitel');
    await teacher.locator('#password').fill('ucitel123');
    await teacher.getByRole('button', { name: 'Přihlásit se' }).click();
    await expect(teacher).toHaveURL(/\/skola\/home$/);
    await teacher.goto('/skola/settings');
    await teacher.getByRole('button', { name: 'Zpětná vazba' }).click();
    const form = teacher.getByRole('dialog', {
      name: 'Jak se vám Soovioo líbí?',
    });
    await form.getByRole('radio', { name: 'Ujde to' }).click();
    await form.getByLabel('Co se vám líbí').fill(schoolText);
    await form.getByLabel('Nechte nám kontakt').fill('ucitel-e2e@example.com');
    await form.getByText('Souhlasím, aby mě tým Soovioo kontaktoval.').click();
    await form.getByRole('button', { name: 'Odeslat' }).click();
    await expect(teacher.getByText('Děkujeme za zpětnou vazbu!')).toBeVisible();
    await expect(form).toBeHidden();
  } finally {
    await teacher.close();
  }

  await page.goto('/superadmin/login');
  const api = apiUrl(page);
  const created = await request.post(`${api}/public/demo`, {
    data: { type: 'Elementary', name: 'Zpětná vazba e2e', classesPerGrade: 1 },
  });
  expect(created.ok()).toBe(true);
  const { slug } = (await created.json()) as { slug: string };
  const text = `Chybí mi tisk rozvrhu ${slug}`;
  expect(
    (
      await request.post(`${api}/public/demo/${slug}/feedback`, {
        data: {
          rating: 4,
          text,
          email: 'e2e-feedback@example.com',
          consent: true,
          role: 'Teacher',
        },
      })
    ).ok(),
  ).toBe(true);
  expect((await request.delete(`${api}/public/demo/${slug}`)).ok()).toBe(true);

  await page.locator('#username').fill('superadmin');
  await page.locator('#password').fill('superadmin123');
  await page.getByRole('button', { name: 'Přihlásit se' }).click();
  await expect(page).toHaveURL(/\/superadmin$/);
  await expect(page.getByText('Dema — kontakty')).toBeVisible();
  // Feedback is no longer a table on Správa škol.
  await expect(page.getByText('Dema — zpětná vazba')).toHaveCount(0);

  await page.getByRole('link', { name: 'Zpětná vazba' }).click();
  await expect(page).toHaveURL(/\/superadmin\/feedback$/);
  await expect.poll(() => badgeCount(page)).toBeGreaterThan(0);
  const unseenBefore = await badgeCount(page);

  await page.getByLabel('Hledat').fill(slug);
  const row = page.locator('tbody tr').filter({ hasText: slug });
  await expect(page.locator('tbody tr')).toHaveCount(1);
  await expect(row).toContainText('(smazáno)');
  await expect(row).toContainText('Učitel');
  await expect(row).toContainText('e2e-feedback@example.com');
  await expect(row).toContainText('Nové');

  await row.click();
  const dialog = page.getByRole('dialog', { name: 'Zpětná vazba' });
  await expect(dialog).toContainText(text);
  await expect(dialog).toContainText('souhlas s kontaktem: ano');
  await expect(dialog.getByLabel('Odpověď e-mailem')).toBeVisible();
  await expect(row).not.toContainText('Nové');
  await expect.poll(() => badgeCount(page)).toBe(unseenBefore - 1);

  await dialog.getByRole('button', { name: 'Označit jako důležité' }).click();
  await expect(dialog.getByRole('button', { name: 'Důležité' })).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(dialog).toBeHidden();
  await expect(row).toContainText('Důležité');

  await page.getByRole('combobox', { name: 'Odkud' }).click();
  await page.getByRole('option', { name: 'Ze škol' }).click();
  await expect(page.getByText('Nic neodpovídá filtrům.')).toBeVisible();
  await page.getByRole('combobox', { name: 'Odkud' }).click();
  await page.getByRole('option', { name: 'Z dem' }).click();
  await expect(row).toBeVisible();

  await page.getByRole('combobox', { name: 'Stav' }).click();
  await page.getByRole('option', { name: 'Vyřízené', exact: true }).click();
  await expect(page.getByText('Nic neodpovídá filtrům.')).toBeVisible();
  await page.getByRole('combobox', { name: 'Stav' }).click();
  await page.getByRole('option', { name: 'Důležité', exact: true }).click();
  await expect(row).toBeVisible();

  await row.click();
  await dialog.getByRole('button', { name: 'Označit jako neviděné' }).click();
  await expect(dialog).toBeHidden();
  await expect(row).toContainText('Nové');
  await expect.poll(() => badgeCount(page)).toBe(unseenBefore);

  await row.click();
  await dialog.getByRole('button', { name: 'Smazat' }).click();
  await page
    .getByRole('alertdialog')
    .getByRole('button', { name: 'Smazat' })
    .click();
  await expect(page.getByText('Zpětná vazba smazána.')).toBeVisible();
  await expect(page.getByText('Nic neodpovídá filtrům.')).toBeVisible();
  await expect.poll(() => badgeCount(page)).toBe(unseenBefore - 1);

  await page.getByRole('button', { name: 'Zrušit filtry' }).click();
  await page.getByLabel('Hledat').fill(schoolText);
  const schoolRow = page.locator('tbody tr').filter({ hasText: schoolText });
  await expect(page.locator('tbody tr')).toHaveCount(1);
  await expect(schoolRow).not.toContainText('demo');
  await expect(schoolRow).toContainText('Učitel');
  await expect(schoolRow).toContainText('ucitel-e2e@example.com');
  await schoolRow.click();
  await expect(dialog).toContainText('souhlas s kontaktem: ano');
  await expect(dialog.getByLabel('Odpověď e-mailem')).toBeVisible();
});

import { Locator, Page } from '@playwright/test';
import { expect, test } from './fixtures';
import { versionsApi } from './timetable-versions';

/** An InputNumber takes typing, not fill() — that keeps what's there. */
async function setNumber(field: Locator, value: string) {
  await field.click();
  await field.evaluate((input: HTMLInputElement) =>
    input.setSelectionRange(input.value.length, input.value.length),
  );
  for (let i = 0; i < 3; i++) {
    await field.press('Backspace');
  }
  await field.pressSequentially(value);
  await expect(field).toHaveValue(value);
}

async function edit(
  page: Page,
  row: Locator,
  label: string,
  from: string,
  to: string,
) {
  await row.getByRole('button', { name: `Upravit — ${label}` }).click();
  await setNumber(
    page.getByRole('spinbutton', { name: `Od hodiny — ${label}` }),
    from,
  );
  await setNumber(
    page.getByRole('spinbutton', { name: `Do hodiny — ${label}` }),
    to,
  );
  await page.getByRole('button', { name: `Uložit — ${label}` }).click();
}

// A class of its own in a grade no other test uses: the school's block and
// the other grades stay as they are.
test('a grade takes the school’s main block, a class its grade’s, both can have their own', async ({
  page,
  request,
}) => {
  const name = `Blok${Date.now()}`;
  await page.goto('/skola/login');
  await page.locator('#username').fill('admin');
  await page.locator('#password').fill('admin123');
  await page.getByRole('button', { name: 'Přihlásit se' }).click();
  await expect(page).toHaveURL(/\/skola\/home$/);
  const admin = await versionsApi(page, request);
  const created = await request.post(`${admin.api}/school-classes`, {
    headers: admin.headers,
    data: { name, rocnik: 12 },
  });
  expect(created.ok()).toBeTruthy();
  const { id } = await created.json();
  try {
    await page.goto('/skola/admin?group=teaching&section=settings');
    const card = page.locator('p-card', { hasText: 'Hlavní výukový blok' });
    await expect(
      card.getByRole('spinbutton', { name: 'Škola od hodiny' }),
    ).toHaveValue('1');
    await expect(
      card.getByRole('spinbutton', { name: 'Škola do hodiny' }),
    ).toHaveValue('4');
    const grade = card
      .getByRole('table', { name: 'Ročníky' })
      .getByRole('row', { name: /^12\. ročník/ });
    const schoolClass = card
      .getByRole('table', { name: 'Třídy' })
      .getByRole('row', { name: new RegExp(`^${name}`) });
    await expect(grade).toContainText('1.–4. hodina (podle školy)');
    await expect(schoolClass).toContainText('1.–4. hodina (podle školy)');

    await edit(page, grade, '12. ročník', '2', '5');
    await expect(grade).toHaveText(/2\.–5\. hodina\s*$/);
    await expect(schoolClass).toContainText('2.–5. hodina (podle ročníku)');

    await edit(page, schoolClass, name, '1', '3');
    await expect(schoolClass).toHaveText(/1\.–3\. hodina\s*$/);

    await schoolClass
      .getByRole('button', { name: `Podle ročníku — ${name}` })
      .click();
    await expect(schoolClass).toContainText('2.–5. hodina (podle ročníku)');
    await grade
      .getByRole('button', { name: 'Podle školy — 12. ročník' })
      .click();
    await expect(schoolClass).toContainText('1.–4. hodina (podle školy)');
  } finally {
    await request.delete(`${admin.api}/school-classes/${id}`, {
      headers: admin.headers,
    });
    await request.delete(`${admin.api}/main-blocks/grades/12`, {
      headers: admin.headers,
    });
  }
});

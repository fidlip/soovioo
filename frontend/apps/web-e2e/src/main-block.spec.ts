import { expect, test } from './fixtures';
import { archive, versionsApi } from './timetable-versions';

// An empty draft of its own: the school's block stays as it is, the grade's
// and the class's change only in that version.
test('the school’s main block in Nastavení, a grade’s in Učební plán, a class’s in Třídy', async ({
  page,
  request,
}) => {
  const version = `E2E blok ${Date.now()}`;
  await page.goto('/skola/login');
  await page.locator('#username').fill('admin');
  await page.locator('#password').fill('admin123');
  await page.getByRole('button', { name: 'Přihlásit se' }).click();
  await expect(page).toHaveURL(/\/skola\/home$/);
  const admin = await versionsApi(page, request);
  const created = await request.post(`${admin.api}/timetable-versions`, {
    headers: admin.headers,
    data: { name: version, validFrom: '2099-09-01' },
  });
  expect(created.ok()).toBeTruthy();
  const { id } = await created.json();
  try {
    await page.goto('/skola/admin?group=teaching&section=settings');
    const card = page.locator('p-card', {
      hasText: 'Hlavní výukový blok školy',
    });
    await expect(card.getByRole('spinbutton', { name: 'Od hodiny' })).toHaveValue(
      '1',
    );
    await expect(card.getByRole('spinbutton', { name: 'Do hodiny' })).toHaveValue(
      '4',
    );
    await expect(
      page.getByRole('combobox', { name: 'Verze rozvrhu' }),
    ).toHaveCount(0);

    await page.getByRole('tab', { name: 'Učební plán' }).click();
    await page.getByRole('combobox', { name: 'Verze rozvrhu' }).click();
    await page.getByRole('option', { name: version }).click();
    const plan = page.getByRole('table', { name: 'Učební plán' });
    const gradeTo = plan.getByRole('spinbutton', {
      name: 'Do hodiny — 5. ročník',
    });
    await expect(gradeTo).toHaveValue('');
    await expect(gradeTo).toHaveAttribute('placeholder', '4');
    await gradeTo.fill('5');
    await page.getByRole('button', { name: 'Uložit plán' }).click();
    await expect(page.getByText('Učební plán je uložený.')).toBeVisible();
    await expect(
      plan.getByRole('spinbutton', { name: 'Od hodiny — 5. ročník' }),
    ).toHaveValue('1');
    await expect(
      plan.getByRole('button', { name: 'Podle školy — 5. ročník' }),
    ).toBeVisible();

    await page.getByRole('tab', { name: 'Třídy' }).click();
    const block = page
      .getByRole('region', { name: 'Třída 5.A' })
      .getByRole('group', { name: 'Hlavní výukový blok — 5.A' });
    const classFrom = block.getByRole('spinbutton', { name: 'Od hodiny — 5.A' });
    await expect(classFrom).toHaveAttribute('placeholder', '1');
    await expect(
      block.getByRole('spinbutton', { name: 'Do hodiny — 5.A' }),
    ).toHaveAttribute('placeholder', '5');
    await expect(block).toContainText('podle ročníku');

    await classFrom.fill('2');
    await classFrom.press('Enter');
    await expect(block).toContainText('vlastní · ročník 1.–5. hodina');
    await expect(
      block.getByRole('spinbutton', { name: 'Do hodiny — 5.A' }),
    ).toHaveValue('5');

    await block.getByRole('button', { name: 'Podle ročníku — 5.A' }).click();
    await expect(block).toContainText('podle ročníku');
    await expect(classFrom).toHaveValue('');
  } finally {
    await archive(admin, id);
  }
});

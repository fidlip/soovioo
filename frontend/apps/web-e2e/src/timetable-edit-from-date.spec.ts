import { expect, test } from './fixtures';
import { archive, pickVersion, versionsApi } from './timetable-versions';

function typed(date: Date): string {
  const pad = (n: number) => `${n}`.padStart(2, '0');
  return `${pad(date.getDate())}.${pad(date.getMonth() + 1)}.${date.getFullYear()}`;
}

function daysAhead(days: number): Date {
  const date = new Date();
  date.setDate(date.getDate() + days);
  return date;
}

function nextMonday(): Date {
  return daysAhead((8 - new Date().getDay()) % 7 || 7);
}

test('the version that applies is edited from a date: a copy, the same one again for that date, never from the past', async ({
  page,
  request,
}) => {
  await page.goto('/skola/login');
  await page.locator('#username').fill('admin');
  await page.locator('#password').fill('admin123');
  await page.getByRole('button', { name: 'Přihlásit se' }).click();
  await expect(page).toHaveURL(/\/skola\/home$/);
  const admin = await versionsApi(page, request);
  await page.goto('/skola/admin?group=teaching&section=timetable');

  const start = daysAhead(7000 + (Math.floor(Date.now() / 1000) % 1000));
  const name = `Změna od ${start.getDate()}. ${start.getMonth() + 1}. ${start.getFullYear()}`;
  const editFromDate = page.getByRole('button', { name: 'Upravit od data' });
  const dialog = page.getByRole('dialog', { name: 'Upravit od data' });
  const validFrom = dialog.getByLabel('Platí od');
  const info = page.getByTestId('timetable-version-info');

  async function openDraft() {
    await editFromDate.click();
    await expect(validFrom).toHaveValue(typed(nextMonday()));
    await validFrom.press('Control+a');
    await validFrom.pressSequentially(typed(start));
    await dialog.getByRole('button', { name: 'Otevřít koncept' }).click();
    await expect(dialog).toBeHidden();
    await expect(info).toContainText(name);
    await expect(info).toContainText('Koncept');
    await expect(editFromDate).toHaveCount(0);
  }
  async function draftsNamed() {
    const versions: { id: number; name: string; status: string }[] = await (
      await request.get(`${admin.api}/timetable-versions`, {
        headers: admin.headers,
      })
    ).json();
    return versions.filter((v) => v.name === name && v.status === 'Draft');
  }

  await editFromDate.click();
  await expect(validFrom).toHaveValue(typed(nextMonday()));
  await validFrom.press('Control+a');
  await validFrom.pressSequentially(typed(daysAhead(-1)));
  await validFrom.press('Tab');
  await expect(validFrom).toHaveValue('');
  await expect(
    dialog.getByRole('button', { name: 'Otevřít koncept' }),
  ).toBeDisabled();
  await page.keyboard.press('Escape');
  await expect(dialog).toBeHidden();

  await openDraft();
  await expect(
    page.getByRole('button', { name: 'Přidat hodinu' }).first(),
  ).toBeAttached();
  const [draft] = await draftsNamed();
  try {
    await pickVersion(page, 'Platí');
    await expect(page.getByTestId('timetable-version-read-only')).toBeVisible();

    await openDraft();
    expect(await draftsNamed()).toEqual([draft]);
  } finally {
    await archive(admin, draft.id);
  }
});
